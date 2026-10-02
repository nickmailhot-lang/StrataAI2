using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<CardMemberOption>> ListCardMemberOptionsAsync(Guid cardId, Guid? after, bool requireVerifiedEmail,
        CancellationToken cancellationToken = default)
    {
        var card = await FindCardAsync(cardId, cancellationToken); if (card is null) return [];
        if (!connectionFactory.HasCommandScope(card.OrganizationId)) throw new InvalidOperationException("Card member options require the owning Board transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(card.OrganizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT m.user_id,u.display_name,a.user_id IS NOT NULL
            FROM board_members m JOIN organization_members o ON o.tenant_id=m.tenant_id AND o.user_id=m.user_id
            JOIN users u ON u.id=m.user_id
            LEFT JOIN card_members a ON a.tenant_id=m.tenant_id AND a.board_id=m.board_id AND a.user_id=m.user_id AND a.card_id=@card
            WHERE m.tenant_id=@tenant AND m.board_id=@board AND m.status='ACTIVE' AND o.status='ACTIVE'
                AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified) AND (@after IS NULL OR m.user_id>@after)
            ORDER BY m.user_id LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", card.OrganizationId); query.Parameters.AddWithValue("board", card.BoardId);
        query.Parameters.AddWithValue("card", cardId); query.Parameters.AddWithValue("verified", requireVerifiedEmail);
        query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<CardMemberOption>(); await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2)));
        return result;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public async Task<IReadOnlyList<CardMemberOption>> ListCardMemberOptionsAsync(Guid cardId, Guid? after, bool requireVerifiedEmail,
        CancellationToken cancellationToken = default)
    {
        CardRecord? card; lock (_sync) { if (!_cards.TryGetValue(cardId, out card)) return []; }
        var members = await ListAssignableBoardMembersAsync(card.BoardId, after, requireVerifiedEmail, cancellationToken);
        lock (_sync) return members.Select(m => new CardMemberOption(m.UserId, m.DisplayName, _cardMembers.ContainsKey((cardId, m.UserId)))).ToArray();
    }
}
