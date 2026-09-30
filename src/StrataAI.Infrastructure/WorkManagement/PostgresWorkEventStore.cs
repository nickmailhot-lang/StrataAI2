using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using System.Text.Json;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresWorkEventStore(PostgresConnectionFactory connections,
    PostgresBackgroundJobStore jobs) : IWorkEventStore
{
    public async Task AppendAsync(WorkEvent change, CancellationToken cancellationToken = default)
    {
        if (!connections.HasCommandScope(change.OrganizationId))
            throw new InvalidOperationException("Work events require the owning command transaction.");
        await using var session = await connections.OpenTenantSessionAsync(change.OrganizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", change.OrganizationId);
            query.Parameters.AddWithValue("board", change.BoardId);
            query.Parameters.AddWithValue("event", change.EventId);
            return query;
        }
        await using (var create = Query("INSERT INTO work_event_streams(tenant_id,board_id) VALUES (@tenant,@board) ON CONFLICT DO NOTHING;"))
            await create.ExecuteNonQueryAsync(cancellationToken);
        await using (var gate = Query("SELECT last_sequence FROM work_event_streams WHERE tenant_id=@tenant AND board_id=@board FOR UPDATE;"))
            await gate.ExecuteScalarAsync(cancellationToken);
        await using (var existing = Query("SELECT board_id,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id FROM work_events WHERE tenant_id=@tenant AND event_id=@event;"))
        await using (var reader = await existing.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetGuid(0) != change.BoardId || reader.GetGuid(1) != change.ActorId ||
                    reader.GetString(2) != change.EventType || reader.GetString(3) != change.EntityType ||
                    reader.GetGuid(4) != change.EntityId || reader.GetInt64(5) != change.Version ||
                    reader.GetString(6) != change.CorrelationId)
                    throw new InvalidOperationException("Work event identity was reused.");
                return;
            }
        }
        long sequence;
        await using (var allocate = Query("UPDATE work_event_streams SET last_sequence=last_sequence+1,updated_at=clock_timestamp() WHERE tenant_id=@tenant AND board_id=@board RETURNING last_sequence;"))
            sequence = (long)(await allocate.ExecuteScalarAsync(cancellationToken))!;
        await using (var append = Query("""
            INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
            VALUES (@tenant,@event,@board,@sequence,@actor,@type,@entity_type,@entity,@version,@correlation,@created);
            """))
        {
            append.Parameters.AddWithValue("sequence", sequence);
            append.Parameters.AddWithValue("actor", change.ActorId);
            append.Parameters.AddWithValue("type", change.EventType);
            append.Parameters.AddWithValue("entity_type", change.EntityType);
            append.Parameters.AddWithValue("entity", change.EntityId);
            append.Parameters.AddWithValue("version", change.Version);
            append.Parameters.AddWithValue("correlation", change.CorrelationId);
            append.Parameters.AddWithValue("created", change.CreatedAt);
            await append.ExecuteNonQueryAsync(cancellationToken);
        }
        await jobs.PublishAsync(session, new NewBackgroundJob(Guid.NewGuid(), change.OrganizationId,
            WorkEventDeliveryHandler.Type, $"work-event/{change.EventId:N}", change.ActorId,
            WorkEventDeliveryHandler.Service, change.CorrelationId,
            JsonSerializer.Serialize(new { eventId = change.EventId, boardId = change.BoardId })), cancellationToken);
        await session.CommitAsync(cancellationToken);
    }
}
