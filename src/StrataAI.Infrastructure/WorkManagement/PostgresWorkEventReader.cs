using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresWorkEventReader(PostgresConnectionFactory connections) : IWorkEventReader
{
    public async Task<WorkEventReadPage> ReadAsync(Guid organizationId, Guid boardId, long since, int limit,
        CancellationToken cancellationToken = default)
    {
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", organizationId);
            command.Parameters.AddWithValue("board", boardId);
            return command;
        }
        long published;
        await using (var stream = Query("SELECT last_sequence FROM work_event_streams WHERE tenant_id=@tenant AND board_id=@board;"))
            published = await stream.ExecuteScalarAsync(cancellationToken) is long value ? value : 0;
        var rows = new List<WorkEventReadCandidate>();
        await using (var read = Query("""
            SELECT e.sequence,e.event_id,e.actor_id,e.event_type,e.entity_type,e.entity_id,
                   e.entity_version,e.correlation_id,e.created_at,e.ready_at IS NOT NULL,
                   CASE e.entity_type
                     WHEN 'Board' THEN e.entity_id=@board
                     WHEN 'List' THEN EXISTS(SELECT 1 FROM board_lists l
                       WHERE l.tenant_id=@tenant AND l.board_id=@board AND l.id=e.entity_id AND l.lifecycle_state='ACTIVE')
                     WHEN 'Card' THEN EXISTS(SELECT 1 FROM cards c JOIN board_lists l
                       ON l.tenant_id=c.tenant_id AND l.board_id=c.board_id AND l.id=c.list_id
                       WHERE c.tenant_id=@tenant AND c.board_id=@board AND c.id=e.entity_id
                         AND c.lifecycle_state='ACTIVE' AND l.lifecycle_state='ACTIVE')
                     ELSE false END
            FROM work_events e WHERE e.tenant_id=@tenant AND e.board_id=@board
              AND e.sequence>@since AND e.sequence<=@published
            ORDER BY e.sequence LIMIT @limit;
            """))
        {
            read.Parameters.AddWithValue("since", since);
            read.Parameters.AddWithValue("published", published);
            read.Parameters.AddWithValue("limit", limit + 1);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetInt64(0), new(reader.GetGuid(1), organizationId, boardId,
                    reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetGuid(5),
                    reader.GetInt64(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8)),
                    reader.GetBoolean(9), reader.GetBoolean(10)));
        }
        await session.CommitAsync(cancellationToken);
        return WorkEventReadWindow.Build(since, published, limit, rows);
    }
}
