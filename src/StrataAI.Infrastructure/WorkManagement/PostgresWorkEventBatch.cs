using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkEventStore
{
    public async Task AppendBatchAsync(IReadOnlyList<WorkEvent> changes, CancellationToken ct = default)
    {
        var snapshot = changes.ToArray();
        if (snapshot.Length == 0) return;
        var tenant = snapshot[0].OrganizationId;
        if (!connections.HasCommandScope(tenant) || snapshot.Any(e => e.OrganizationId != tenant))
            throw new InvalidOperationException("Work event batches require one owning tenant command.");
        var unique = new Dictionary<Guid, WorkEvent>();
        foreach (var change in snapshot)
        {
            if (unique.TryGetValue(change.EventId, out var prior) && !SameIdentity(prior, change))
                throw new InvalidOperationException("Work event identity was reused.");
            unique.TryAdd(change.EventId, change);
        }
        await using var session = await connections.OpenTenantSessionAsync(tenant, ct);
        var boards = unique.Values.Select(e => e.BoardId).Distinct().Order().ToArray();
        NpgsqlCommand Query(string sql)
        {
            var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant);
            return query;
        }
        await using (var create = Query("INSERT INTO work_event_streams(tenant_id,board_id) SELECT @tenant,b FROM unnest(@boards) b ORDER BY b ON CONFLICT DO NOTHING;"))
        {
            create.Parameters.AddWithValue("boards", boards); await create.ExecuteNonQueryAsync(ct);
        }
        await using (var gate = Query("SELECT board_id FROM work_event_streams WHERE tenant_id=@tenant AND board_id=ANY(@boards) ORDER BY board_id FOR UPDATE;"))
        {
            gate.Parameters.AddWithValue("boards", boards); await gate.ExecuteNonQueryAsync(ct);
        }
        await using (var existing = Query("SELECT event_id,board_id,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id FROM work_events WHERE tenant_id=@tenant AND event_id=ANY(@ids);"))
        {
            existing.Parameters.AddWithValue("ids", unique.Keys.ToArray());
            await using var reader = await existing.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var change = unique[reader.GetGuid(0)];
                if (change.BoardId != reader.GetGuid(1) || change.ActorId != reader.GetGuid(2)
                    || change.EventType != reader.GetString(3) || change.EntityType != reader.GetString(4)
                    || change.EntityId != reader.GetGuid(5) || change.Version != reader.GetInt64(6)
                    || change.CorrelationId != reader.GetString(7))
                    throw new InvalidOperationException("Work event identity was reused.");
                unique.Remove(change.EventId);
            }
        }
        if (unique.Count == 0) return;
        var counts = unique.Values.GroupBy(e => e.BoardId).ToDictionary(g => g.Key, g => (long)g.Count());
        var sequences = new Dictionary<Guid, long>();
        await using (var allocate = Query("""
            UPDATE work_event_streams s SET last_sequence=s.last_sequence+x.count,updated_at=clock_timestamp()
            FROM jsonb_to_recordset(@counts) AS x(board uuid,count bigint)
            WHERE s.tenant_id=@tenant AND s.board_id=x.board RETURNING s.board_id,s.last_sequence-x.count;
            """))
        {
            allocate.Parameters.AddWithValue("counts", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(counts.Select(p => new { board = p.Key, count = p.Value })));
            await using var reader = await allocate.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) sequences.Add(reader.GetGuid(0), reader.GetInt64(1));
        }
        var payload = JsonSerializer.Serialize(unique.Values.Select(e => new
        {
            id = e.EventId, board = e.BoardId, sequence = ++sequences[e.BoardId], actor = e.ActorId,
            type = e.EventType, entity_type = e.EntityType, entity = e.EntityId, version = e.Version,
            correlation = e.CorrelationId, created = e.CreatedAt
        }).ToArray());
        await using (var append = Query("""
            INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
            SELECT @tenant,id,board,sequence,actor,type,entity_type,entity,version,correlation,created
            FROM jsonb_to_recordset(@rows) AS x(id uuid,board uuid,sequence bigint,actor uuid,type text,
                entity_type text,entity uuid,version bigint,correlation text,created timestamptz);
            """))
        {
            append.Parameters.AddWithValue("rows", NpgsqlDbType.Jsonb, payload); await append.ExecuteNonQueryAsync(ct);
        }
        var publications = unique.Values.Select(e => new NewBackgroundJob(Guid.NewGuid(), tenant,
            WorkEventDeliveryHandler.Type, $"work-event/{e.EventId:N}", e.ActorId, WorkEventDeliveryHandler.Service,
            e.CorrelationId, JsonSerializer.Serialize(new { eventId = e.EventId, boardId = e.BoardId }))).ToArray();
        if (await jobs.PublishBatchAsync(session, publications, ct) != publications.Length)
            throw new InvalidOperationException("New Work events did not retain every delivery job.");
        // This session borrows the owning command. Commit/disposal cannot
        // independently publish its events, streams, jobs or activity triggers.
    }

    private static bool SameIdentity(WorkEvent a, WorkEvent b) => a.OrganizationId == b.OrganizationId
        && a.BoardId == b.BoardId && a.ActorId == b.ActorId && a.EventType == b.EventType
        && a.EntityType == b.EntityType && a.EntityId == b.EntityId && a.Version == b.Version
        && a.CorrelationId == b.CorrelationId;
}
