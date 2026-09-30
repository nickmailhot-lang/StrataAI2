using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresWorkEventDeliveryStore(PostgresConnectionFactory connections) : IWorkEventDeliveryStore
{
    public async Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid boardId, Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", job.OrganizationId);
            query.Parameters.AddWithValue("board", boardId);
            query.Parameters.AddWithValue("event", eventId);
            query.Parameters.AddWithValue("actor", job.ActorId);
            query.Parameters.AddWithValue("job", job.Id);
            query.Parameters.AddWithValue("lease", job.LeaseId);
            query.Parameters.AddWithValue("worker", job.WorkerId);
            return query;
        }
        // Lock the current claim before producing an effect. A superseded or
        // expired lease cannot mark readiness, even when its event already exists.
        await using (var claim = Query("""
            SELECT id FROM background_jobs WHERE tenant_id=@tenant AND id=@job
            AND actor_id=@actor AND job_type='WORK_EVENT_READY' AND service_identity='work-event-delivery'
            AND state='RUNNING' AND lease_id=@lease AND worker_id=@worker
            AND lease_expires_at>clock_timestamp() FOR UPDATE;
            """))
            if (await claim.ExecuteScalarAsync(cancellationToken) is null) return false;
        await using (var ready = Query("""
            UPDATE work_events SET ready_at=clock_timestamp()
            WHERE tenant_id=@tenant AND board_id=@board AND event_id=@event AND actor_id=@actor AND ready_at IS NULL
            AND EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id=@tenant AND id=@job
                AND lease_expires_at>clock_timestamp());
            """))
            await ready.ExecuteNonQueryAsync(cancellationToken);
        bool exists;
        await using (var verify = Query("""
            SELECT EXISTS(SELECT 1 FROM work_events
            WHERE tenant_id=@tenant AND board_id=@board AND event_id=@event AND actor_id=@actor AND ready_at IS NOT NULL)
            AND EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id=@tenant AND id=@job
                AND lease_expires_at>clock_timestamp());
            """))
            exists = (bool)(await verify.ExecuteScalarAsync(cancellationToken))!;
        await session.CommitAsync(cancellationToken);
        return exists;
    }
}
