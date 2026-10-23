using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyDictionary<Guid, CardMemberPreview>> ListCardMemberPreviewsAsync(Guid boardId, bool requireVerifiedEmail,
        CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken); if (board is null) return new Dictionary<Guid, CardMemberPreview>();
        if (!connectionFactory.HasCommandScope(board.OrganizationId)) throw new InvalidOperationException("Member previews require the owning Board transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT c.id,ranked.user_id,ranked.display_name,COALESCE(ranked.total,0),c.version
            FROM cards c JOIN board_lists p ON p.tenant_id=c.tenant_id AND p.board_id=c.board_id AND p.id=c.list_id
            LEFT JOIN (
              SELECT a.card_id,a.user_id,u.display_name,count(*) OVER(PARTITION BY a.card_id) AS total,
                row_number() OVER(PARTITION BY a.card_id ORDER BY a.user_id) AS position
              FROM card_members a JOIN board_members m ON m.tenant_id=a.tenant_id AND m.board_id=a.board_id AND m.user_id=a.user_id
              JOIN organization_members o ON o.tenant_id=a.tenant_id AND o.user_id=a.user_id JOIN users u ON u.id=a.user_id
              WHERE a.tenant_id=@tenant AND a.board_id=@board AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE'
                AND (NOT @verified OR u.email_verified)
            ) ranked ON ranked.card_id=c.id AND ranked.position<=6
            WHERE c.tenant_id=@tenant AND c.board_id=@board AND c.lifecycle_state='ACTIVE' AND p.lifecycle_state='ACTIVE'
            ORDER BY c.id,ranked.position;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", board.OrganizationId); query.Parameters.AddWithValue("board", boardId);
        query.Parameters.AddWithValue("verified", requireVerifiedEmail);
        var result = new Dictionary<Guid, CardMemberPreview>(); await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (!result.TryGetValue(id, out var preview)) result[id] = preview = new(new List<CardMemberIndicator>(), reader.GetInt64(3), reader.GetInt64(4));
            // An empty preview and its Card revision come from this same SQL
            // statement. Never infer zero from a missing or older snapshot.
            if (!reader.IsDBNull(1))
                ((List<CardMemberIndicator>)preview.Items).Add(new(reader.GetGuid(1), reader.GetString(2)));
        }
        return result;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public async Task<IReadOnlyDictionary<Guid, CardMemberPreview>> ListCardMemberPreviewsAsync(Guid boardId, bool requireVerifiedEmail,
        CancellationToken cancellationToken = default)
    {
        BoardRecord? board; Guid[] members;
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out board)) return new Dictionary<Guid, CardMemberPreview>();
            members = _members.Values.Where(m => m.BoardId == boardId && m.Active).Select(m => m.UserId).Order().ToArray();
        }
        var eligible = new Dictionary<Guid, string>();
        foreach (var batch in members.Chunk(51))
        {
            var profiles = await organizations.ListActiveMembersAsync(board.OrganizationId, null, cancellationToken, userIds: batch);
            foreach (var p in profiles.Where(p => p.AccountStatus == AccountStatus.Active && (!requireVerifiedEmail || p.EmailVerified))) eligible[p.UserId] = p.DisplayName;
        }
        lock (_sync)
        {
            var visible = _cards.Values.Where(c => c.BoardId == boardId && c.LifecycleState == WorkItemLifecycleState.Active
                && _lists.TryGetValue(c.ListId, out var p) && p.LifecycleState == WorkItemLifecycleState.Active).ToDictionary(c => c.Id);
            var assignments = _cardMembers.Keys.Where(a => visible.ContainsKey(a.CardId) && eligible.ContainsKey(a.UserId))
                .GroupBy(a => a.CardId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.UserId).ToArray());
            return visible.Values.ToDictionary(c => c.Id, c =>
            {
                var current = assignments.GetValueOrDefault(c.Id) ?? [];
                return new CardMemberPreview(current.Take(6).Select(a => new CardMemberIndicator(a.UserId, eligible[a.UserId])).ToArray(), current.LongLength, c.Version);
            });
        }
    }
}
