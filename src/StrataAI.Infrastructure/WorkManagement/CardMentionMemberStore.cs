using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresCardMentionMemberStore(PostgresConnectionFactory connections) : ICardMentionMemberStore
{
    private void RequireScope(Guid organization)
    {
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Mention members require an owning authorized Work transaction.");
    }
    public Task<IReadOnlyList<CardMentionMember>> SearchAsync(Guid organization, Guid board, string prefix, string? after,
        bool requireVerifiedEmail, CancellationToken ct = default)
    {
        RequireScope(organization); CardMentionLookup.Search(prefix, after);
        return Read(organization, board, prefix, after, null, requireVerifiedEmail, ct);
    }
    public Task<IReadOnlyList<CardMentionMember>> ResolveAsync(Guid organization, Guid board, IReadOnlyList<string> handles,
        bool requireVerifiedEmail, CancellationToken ct = default)
    {
        RequireScope(organization); var targets = CardMentionLookup.Targets(handles);
        return targets.Length == 0 ? Task.FromResult<IReadOnlyList<CardMentionMember>>([])
            : Read(organization, board, "", null, targets, requireVerifiedEmail, ct);
    }
    private async Task<IReadOnlyList<CardMentionMember>> Read(Guid organization, Guid board, string prefix, string? after,
        string[]? handles, bool verified, CancellationToken ct)
    {
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            SELECT m.user_id,h.handle,u.display_name,h.version
            FROM board_members m
            JOIN organization_members o ON o.tenant_id=m.tenant_id AND o.user_id=m.user_id
            JOIN users u ON u.id=m.user_id
            JOIN user_mention_handles h ON h.user_id=m.user_id
            WHERE m.tenant_id=@organization AND m.board_id=@board AND m.status='ACTIVE' AND o.status='ACTIVE'
              AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified)
              AND h.handle>=@prefix AND h.handle<(@prefix||'{') COLLATE "C"
              AND (@after IS NULL OR h.handle>@after)
              AND (@handles IS NULL OR h.handle=ANY(@handles))
            ORDER BY h.handle COLLATE "C" LIMIT @limit;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("organization", organization); query.Parameters.AddWithValue("board", board);
        query.Parameters.AddWithValue("prefix", prefix); query.Parameters.AddWithValue("verified", verified);
        query.Parameters.AddWithValue("after", NpgsqlDbType.Text, (object?)after ?? DBNull.Value);
        query.Parameters.AddWithValue("handles", NpgsqlDbType.Array | NpgsqlDbType.Text, (object?)handles ?? DBNull.Value);
        query.Parameters.AddWithValue("limit", handles is null ? CardMentionLookup.PageSize + 1 : handles.Length);
        var result = new List<CardMentionMember>(); await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct)) result.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2), rows.GetInt64(3)));
        return result;
    }
}

internal sealed class InMemoryCardMentionMemberStore(IWorkManagementStore work, DemoWorkTransactionScope scope,
    DemoMentionHandleRegistry handles) : ICardMentionMemberStore
{
    private void RequireScope(Guid organization)
    {
        if (organization == Guid.Empty || !scope.Owns(organization))
            throw new InvalidOperationException("Mention members require an owning authorized Work transaction.");
    }
    private async Task<IReadOnlyList<CardMentionMember>> Eligible(Guid organization, Guid board, bool verified,
        Func<CardMentionMember, bool> include, int limit, CancellationToken ct)
    {
        var parent = await work.FindBoardAsync(board, ct);
        if (parent?.OrganizationId != organization) return [];
        var result = new SortedDictionary<string, CardMentionMember>(StringComparer.Ordinal); Guid? after = null;
        // Traverse bounded membership pages rather than exposing global registry
        // enumeration. The final output is bounded independently below.
        while (true)
        {
            var members = await work.ListAssignableBoardMembersAsync(board, after, verified, ct);
            foreach (var member in members)
                if (handles.Find(member.UserId) is { } current)
                {
                    var row = new CardMentionMember(member.UserId, current.Handle, member.DisplayName, current.Version);
                    if (!include(row)) continue;
                    result[row.Handle] = row;
                    if (result.Count > limit) result.Remove(result.Last().Key);
                }
            if (members.Count < 51) break;
            after = members[^1].UserId;
        }
        return result.Values.ToArray();
    }
    public async Task<IReadOnlyList<CardMentionMember>> SearchAsync(Guid organization, Guid board, string prefix, string? after,
        bool requireVerifiedEmail, CancellationToken ct = default)
    {
        RequireScope(organization); CardMentionLookup.Search(prefix, after);
        return await Eligible(organization, board, requireVerifiedEmail,
            row => row.Handle.StartsWith(prefix, StringComparison.Ordinal) && (after is null || string.CompareOrdinal(row.Handle, after) > 0), CardMentionLookup.PageSize + 1, ct);
    }
    public async Task<IReadOnlyList<CardMentionMember>> ResolveAsync(Guid organization, Guid board, IReadOnlyList<string> requested,
        bool requireVerifiedEmail, CancellationToken ct = default)
    {
        RequireScope(organization); var targets = CardMentionLookup.Targets(requested).ToHashSet(StringComparer.Ordinal);
        if (targets.Count == 0) return [];
        return await Eligible(organization, board, requireVerifiedEmail, row => targets.Contains(row.Handle), targets.Count, ct);
    }
}
