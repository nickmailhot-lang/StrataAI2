using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresCardMassMentionMemberStore(PostgresConnectionFactory connections) : ICardMassMentionMemberStore
{
    public async Task<IReadOnlyList<Guid>> LockRecipientsAsync(Guid organization, Guid board, Guid card,
        bool includeCard, bool includeBoard, bool requireVerifiedEmail, CancellationToken ct = default)
    {
        if (organization == Guid.Empty || board == Guid.Empty || card == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Mass mention recipients require the owning Work transaction.");
        ct.ThrowIfCancellationRequested();
        if (!includeCard && !includeBoard) return [];
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var parent = new NpgsqlCommand("SELECT id FROM cards WHERE tenant_id=@tenant AND board_id=@board AND id=@card FOR UPDATE;", session.Connection, session.Transaction);
        parent.Parameters.AddWithValue("tenant", organization); parent.Parameters.AddWithValue("board", board); parent.Parameters.AddWithValue("card", card);
        if (await parent.ExecuteScalarAsync(ct) is not Guid) return [];
        await using var query = new NpgsqlCommand("""
            SELECT m.user_id FROM board_members m
            JOIN organization_members o ON o.tenant_id=m.tenant_id AND o.user_id=m.user_id
            JOIN users u ON u.id=m.user_id
            WHERE m.tenant_id=@tenant AND m.board_id=@board AND m.status='ACTIVE' AND o.status='ACTIVE'
              AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified)
              AND (@all OR EXISTS(SELECT 1 FROM card_members a
                WHERE a.tenant_id=m.tenant_id AND a.board_id=m.board_id AND a.card_id=@card AND a.user_id=m.user_id))
            ORDER BY m.user_id FOR SHARE OF m,o,u;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("board", board); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("verified", requireVerifiedEmail); query.Parameters.AddWithValue("all", includeBoard);
        var ids = new List<Guid>(); await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct)) ids.Add(rows.GetGuid(0));
        return Array.AsReadOnly(ids.Order().ToArray());
    }
}
internal sealed class InMemoryCardMassMentionMemberStore(IWorkManagementStore work, DemoWorkTransactionScope scope) : ICardMassMentionMemberStore
{
    public async Task<IReadOnlyList<Guid>> LockRecipientsAsync(Guid organization, Guid board, Guid card,
        bool includeCard, bool includeBoard, bool requireVerifiedEmail, CancellationToken ct = default)
    {
        if (organization == Guid.Empty || board == Guid.Empty || card == Guid.Empty || !scope.Owns(organization))
            throw new InvalidOperationException("Mass mention recipients require the owning Work transaction.");
        ct.ThrowIfCancellationRequested(); var parent = await work.FindCardAsync(card, ct);
        if (parent?.OrganizationId != organization || parent.BoardId != board || !includeCard && !includeBoard) return [];
        var ids = new HashSet<Guid>(); Guid? after = null;
        while (true)
        {
            // These are bounded storage windows, never a fanout truncation.
            var page = includeBoard
                ? (await work.ListAssignableBoardMembersAsync(board, after, requireVerifiedEmail, ct)).Select(row => row.UserId).ToArray()
                : (await work.ListCardMembersAsync(card, after, requireVerifiedEmail, ct)).Select(row => row.UserId).ToArray();
            foreach (var id in page) ids.Add(id);
            if (page.Length < 51) break;
            after = page[^1]; ct.ThrowIfCancellationRequested();
        }
        return Array.AsReadOnly(ids.Order().ToArray());
    }
}
