using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<AssignableBoardMember>> ListAssignableBoardMembersAsync(Guid boardId, Guid? after,
        bool requireVerifiedEmail, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken);
        if (board is null) return [];
        if (!connectionFactory.HasCommandScope(board.OrganizationId))
            throw new InvalidOperationException("Assignable member discovery requires the owning authorized Board transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT m.user_id,u.display_name
            FROM board_members m JOIN organization_members o ON o.user_id=m.user_id AND o.tenant_id=m.tenant_id
            JOIN users u ON u.id=m.user_id
            WHERE m.tenant_id=@tenant AND m.board_id=@board AND m.status='ACTIVE' AND o.status='ACTIVE'
                AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified)
                AND (@after IS NULL OR m.user_id>@after)
            ORDER BY m.user_id LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", board.OrganizationId); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("verified", requireVerifiedEmail);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<AssignableBoardMember>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public async Task<IReadOnlyList<AssignableBoardMember>> ListAssignableBoardMembersAsync(Guid boardId, Guid? after,
        bool requireVerifiedEmail, CancellationToken cancellationToken = default)
    {
        BoardRecord? board; BoardMemberRecord[] members;
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out board)) return [];
            members = _members.Values.Where(m => m.BoardId == boardId && m.Active
                && (after is null || m.UserId.CompareTo(after.Value) > 0)).OrderBy(m => m.UserId).ToArray();
        }
        var result = new List<AssignableBoardMember>();
        foreach (var batch in members.Chunk(51))
        {
            var profiles = await organizations.ListActiveMembersAsync(board.OrganizationId, null, cancellationToken,
                userIds: batch.Select(m => m.UserId).ToArray());
            result.AddRange(profiles.Where(p => p.AccountStatus == AccountStatus.Active && (!requireVerifiedEmail || p.EmailVerified))
                .OrderBy(p => p.UserId).Select(p => new AssignableBoardMember(p.UserId, p.DisplayName)));
            if (result.Count >= 51) break;
        }
        return result.Take(51).ToArray();
    }
}
