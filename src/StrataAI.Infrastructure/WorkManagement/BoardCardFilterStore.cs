using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<CardRecord>> FilterBoardCardsAsync(Guid boardId, BoardCardFilter filter, Guid? after, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken);
        if (board is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.id,c.tenant_id,c.board_id,c.list_id,c.title,c.description,c.rank,c.lifecycle_state,c.created_at,c.updated_at,c.version
            FROM cards c JOIN board_lists parent ON parent.id=c.list_id AND parent.board_id=c.board_id AND parent.tenant_id=c.tenant_id
            CROSS JOIN LATERAL (
              SELECT count(*) AS hits FROM card_labels a JOIN board_labels l
                ON l.id=a.label_id AND l.board_id=a.board_id AND l.tenant_id=a.tenant_id
              WHERE a.tenant_id=c.tenant_id AND a.board_id=c.board_id AND a.card_id=c.id
                AND l.status='ACTIVE' AND l.id=ANY(@labels)
            ) matches
            WHERE c.tenant_id=@tenant AND c.board_id=@board AND c.lifecycle_state='ACTIVE' AND parent.lifecycle_state='ACTIVE'
              AND (@after IS NULL OR c.id>@after)
              AND (
                (@keyword='' AND cardinality(@labels)=0)
                OR (@all AND (@keyword='' OR strpos(lower(c.title),lower(@keyword))>0 OR strpos(lower(coalesce(c.description,'')),lower(@keyword))>0)
                    AND matches.hits=cardinality(@labels))
                OR (NOT @all AND ((@keyword<>'' AND (strpos(lower(c.title),lower(@keyword))>0 OR strpos(lower(coalesce(c.description,'')),lower(@keyword))>0))
                    OR matches.hits>0))
              )
            ORDER BY c.id LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", board.OrganizationId); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("labels", NpgsqlDbType.Array | NpgsqlDbType.Uuid, filter.LabelIds.ToArray());
        command.Parameters.AddWithValue("keyword", filter.Keyword); command.Parameters.AddWithValue("all", filter.MatchAll);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<CardRecord>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadCard(reader));
        return result;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<IReadOnlyList<CardRecord>> FilterBoardCardsAsync(Guid boardId, BoardCardFilter filter, Guid? after, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            bool Matches(CardRecord card)
            {
                if (filter.Keyword.Length == 0 && filter.LabelIds.Count == 0) return true;
                var text = card.Title.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase)
                    || (card.Description ?? "").Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase);
                bool Assigned(Guid id) => _labels.TryGetValue(id, out var label) && !label.Deleted
                    && label.OrganizationId == card.OrganizationId && label.BoardId == boardId && _cardLabels.Contains((card.Id, id));
                return filter.MatchAll ? (filter.Keyword.Length == 0 || text) && filter.LabelIds.All(Assigned)
                    : (filter.Keyword.Length > 0 && text) || filter.LabelIds.Any(Assigned);
            }
            return Task.FromResult<IReadOnlyList<CardRecord>>(_cards.Values.Where(card => card.BoardId == boardId
                && card.LifecycleState == WorkItemLifecycleState.Active && _lists.TryGetValue(card.ListId, out var parent)
                && parent.BoardId == card.BoardId && parent.OrganizationId == card.OrganizationId && parent.LifecycleState == WorkItemLifecycleState.Active
                && (after is null || card.Id.CompareTo(after.Value) > 0) && Matches(card)).OrderBy(card => card.Id).Take(51).ToArray());
        }
    }
}
