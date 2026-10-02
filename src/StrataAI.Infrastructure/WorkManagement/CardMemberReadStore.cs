using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<CardAssignee>> ListCardMembersAsync(Guid cardId, Guid? after, bool requireVerifiedEmail,
        CancellationToken cancellationToken = default)
    {
        var card = await FindCardAsync(cardId, cancellationToken); if (card is null) return [];
        if (!connectionFactory.HasCommandScope(card.OrganizationId)) throw new InvalidOperationException("Card assignee reads require the owning Board transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(card.OrganizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT a.user_id,u.display_name,a.assigned_by,a.created_at
            FROM card_members a JOIN board_members m ON m.tenant_id=a.tenant_id AND m.board_id=a.board_id AND m.user_id=a.user_id
            JOIN organization_members o ON o.tenant_id=a.tenant_id AND o.user_id=a.user_id
            JOIN users u ON u.id=a.user_id
            WHERE a.tenant_id=@tenant AND a.board_id=@board AND a.card_id=@card
                AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified)
                AND (@after IS NULL OR a.user_id>@after)
            ORDER BY a.user_id LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", card.OrganizationId); query.Parameters.AddWithValue("board", card.BoardId);
        query.Parameters.AddWithValue("card", cardId); query.Parameters.AddWithValue("verified", requireVerifiedEmail);
        query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<CardAssignee>(); await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return result;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public async Task<IReadOnlyList<CardAssignee>> ListCardMembersAsync(Guid cardId, Guid? after, bool requireVerifiedEmail,
        CancellationToken cancellationToken = default)
    {
        CardRecord? card; (Guid UserId, Guid AssignedBy, DateTimeOffset AssignedAt)[] assignments;
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out card)) return [];
            assignments = _cardMembers.Where(a => a.Key.CardId == cardId && (after is null || a.Key.UserId.CompareTo(after.Value) > 0)
                && _members.TryGetValue((card.BoardId, a.Key.UserId), out var m) && m.Active)
                .OrderBy(a => a.Key.UserId).Select(a => (a.Key.UserId, a.Value.AssignedBy, a.Value.AssignedAt)).ToArray();
        }
        var result = new List<CardAssignee>();
        foreach (var batch in assignments.Chunk(51))
        {
            var profiles = (await organizations.ListActiveMembersAsync(card.OrganizationId, null, cancellationToken,
                userIds: batch.Select(a => a.UserId).ToArray())).ToDictionary(p => p.UserId);
            foreach (var assignment in batch)
                if (profiles.TryGetValue(assignment.UserId, out var p) && p.AccountStatus == AccountStatus.Active && (!requireVerifiedEmail || p.EmailVerified))
                    result.Add(new(assignment.UserId, p.DisplayName, assignment.AssignedBy, assignment.AssignedAt));
            if (result.Count >= 51) break;
        }
        return result.Take(51).ToArray();
    }
}
