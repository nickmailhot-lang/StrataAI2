using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<IReadOnlyList<BoardStarEvent>> ListStarEventsAsync(Guid boardId, Guid userId, long after, int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 51) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_sync) return Task.FromResult<IReadOnlyList<BoardStarEvent>>(_starEvents
            .Where(pair => pair.Key.BoardId == boardId && pair.Key.UserId == userId && pair.Key.Version > after)
            .OrderBy(pair => pair.Key.Version).Take(limit).Select(pair => pair.Value).ToArray());
    }
}
internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<BoardStarEvent>> ListStarEventsAsync(Guid boardId, Guid userId, long after, int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 51) throw new ArgumentOutOfRangeException(nameof(limit));
        var tenant = await ResolveBoardTenantAsync(boardId,cancellationToken);
        if (tenant is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenant.Value,cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT event_id,actor_id,tenant_id,board_id,entity_id,version,created_at FROM board_star_events
            WHERE tenant_id=@tenant AND board_id=@board AND actor_id=@actor AND version>@after
            ORDER BY version LIMIT @limit;
            """,session.Connection,session.Transaction);
        command.Parameters.AddWithValue("tenant",tenant.Value); command.Parameters.AddWithValue("board",boardId);
        command.Parameters.AddWithValue("actor",userId); command.Parameters.AddWithValue("after",after); command.Parameters.AddWithValue("limit",limit);
        var rows = new List<BoardStarEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetGuid(3),reader.GetGuid(4),
                reader.GetInt64(5),reader.GetFieldValue<DateTimeOffset>(6)));
        return rows;
    }
}
