using System.Text.Json;
using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

internal static class WorkEventBatchPersistenceContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var foreign = Guid.NewGuid(); var actor = Guid.NewGuid();
        var source = Guid.NewGuid(); var destination = Guid.NewGuid();
        async Task Sql(string text) { await using var q = new NpgsqlCommand(text, admin); await q.ExecuteNonQueryAsync(ct); }
        async Task<long> Count(string text) { await using var q = new NpgsqlCommand(text, admin); return Convert.ToInt64(await q.ExecuteScalarAsync(ct)); }
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await Sql($$"""
            BEGIN;
            INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
            VALUES ('{{actor}}','{{actor}}@example.test',upper('{{actor}}@example.test'),'Batch actor','unusable-fixture','ACTIVE',true,now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
            VALUES ('{{tenant}}','Batch fixture','{{actor}}','ACTIVE','STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            VALUES (gen_random_uuid(),'{{tenant}}','{{actor}}','OWNER','ACTIVE',now(),now());
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
            VALUES ('{{source}}','{{tenant}}','Batch source',now(),now()),('{{destination}}','{{tenant}}','Batch destination',now(),now());
            COMMIT;
            """);
        await using var db = new PostgresConnectionFactory(apiConnection);
        var events = new PostgresWorkEventStore(db, new PostgresBackgroundJobStore(db));
        // A seventh fractional digit makes JSON timestamp rounding differ from
        // Npgsql's binary microsecond binding unless publication normalizes it.
        var now = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
        WorkEvent Change(Guid board) => new(Guid.NewGuid(), tenant, board, actor, "CARD_MOVED", "Card", Guid.NewGuid(), 2, "work-event-batch-test", now);
        var seed = new[] { Change(source), Change(destination), Change(source) };
        try { await events.AppendBatchAsync(seed, ct); throw new InvalidOperationException("Unowned event batch was accepted."); }
        catch (InvalidOperationException e) when (e.Message == "Work event batches require one owning tenant command.") { }
        await db.ExecuteTenantCommandAsync(tenant, async () => { await events.AppendBatchAsync(seed, ct); return true; }, x => x, ct);
        await db.ExecuteTenantCommandAsync(tenant, async () =>
        {
            await using var session = await db.OpenTenantSessionAsync(tenant, ct);
            await using var q = new NpgsqlCommand("SELECT bool_and(created_at=@created) FROM work_events WHERE tenant_id=@tenant;", session.Connection, session.Transaction);
            q.Parameters.AddWithValue("created", now); q.Parameters.AddWithValue("tenant", tenant);
            Require(await q.ExecuteScalarAsync(ct) is true, "Batched event time differs from binary timestamp binding.");
            return true;
        }, x => x, ct);
        await db.ExecuteTenantCommandAsync(tenant, async () => { await events.AppendBatchAsync(seed.Concat(seed).ToArray(), ct); return true; }, x => x, ct);
        Require(await Count($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == 3, "Duplicate batch repeated Work events.");
        Require(await Count($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}';") == 3, "Duplicate batch repeated delivery jobs.");
        foreach (var malformed in new[]
        {
            new[] { seed[0], seed[0] with { Version = 99 } },
            new[] { Change(destination), seed[0] with { Version = 99 } },
            new[] { Change(source), Change(destination) with { OrganizationId = foreign } }
        })
        {
            try
            {
                await db.ExecuteTenantCommandAsync(tenant, async () => { await events.AppendBatchAsync(malformed, ct); return true; }, x => x, ct);
                throw new InvalidOperationException("Malformed event batch was accepted.");
            }
            catch (InvalidOperationException e) when (e.Message is "Work event identity was reused." or "Work event batches require one owning tenant command.") { }
            Require(await Count($"SELECT sum(last_sequence) FROM work_event_streams WHERE tenant_id='{tenant}';") == 3, "Malformed batch changed stream sequence.");
        }
        await Sql("REVOKE INSERT ON background_jobs FROM strataai_api_runtime;");
        try
        {
            try
            {
                await db.ExecuteTenantCommandAsync(tenant, async () => { await events.AppendBatchAsync([Change(source), Change(destination)], ct); return true; }, x => x, ct);
                throw new InvalidOperationException("Revoked batch job publication was accepted.");
            }
            catch (PostgresException e) when (e.SqlState == "42501") { }
        }
        finally { await Sql("GRANT INSERT ON background_jobs TO strataai_api_runtime;"); }
        Require(await Count($"SELECT sum(last_sequence) FROM work_event_streams WHERE tenant_id='{tenant}';") == 3, "Job refusal retained allocated sequence.");
        Require(await Count($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == 3, "Job refusal retained inserted events.");
        // Synthetic immutable Card references test the journal/outbox, not a
        // graph transfer or HTTP authorization. Every reference is retained.
        var referenceCount = Environment.GetEnvironmentVariable("STRATAAI_POST_MVP_LOAD_TESTS") == "1" ? 100_000 : 20;
        var total = referenceCount * 2L + 3;
        var ids = Enumerable.Range(0, referenceCount).Select(_ => Guid.NewGuid()).ToArray();
        await db.ExecuteTenantCommandAsync(tenant, async () =>
        {
            foreach (var chunk in ids.Chunk(1000))
            {
                var rows = chunk.SelectMany(id => new[] { Change(source) with { EntityId = id }, Change(destination) with { EntityId = id } }).ToArray();
                await events.AppendBatchAsync(rows, ct);
            }
            return true;
        }, x => x, ct);
        Require(await Count($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == total, "Journal was truncated.");
        Require(await Count($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='WORK_EVENT_READY' AND service_identity='work-event-delivery';") == total, "Outbox was truncated.");
        Require(await Count($"SELECT count(*) FROM (SELECT board_id FROM work_events WHERE tenant_id='{tenant}' GROUP BY board_id HAVING count(*)=max(sequence) AND min(sequence)=1 AND count(DISTINCT sequence)=count(*)) x;") == 2, "Large journal lost contiguous per-Board sequence.");
        Require(await Count($"SELECT count(*) FROM work_events e JOIN background_jobs j ON j.tenant_id=e.tenant_id AND j.idempotency_key='work-event/'||replace(e.event_id::text,'-','') WHERE e.tenant_id='{tenant}' AND j.actor_id=e.actor_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id,'boardId',e.board_id);") == total, "Delivery references differ from their source events.");
        await db.ExecuteTenantCommandAsync(foreign, async () =>
        {
            await using var session = await db.OpenTenantSessionAsync(foreign, ct);
            await using var q = new NpgsqlCommand("SELECT count(*) FROM work_events;", session.Connection, session.Transaction);
            Require(Convert.ToInt64(await q.ExecuteScalarAsync(ct)) == 0, "Other tenant disclosed journal events."); return true;
        }, x => x, ct);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var queue = new PostgresBackgroundJobStore(worker); var workerId = Guid.NewGuid();
        var job = await queue.ClaimAsync(tenant, workerId, ct);
        Require(job is not null, "Worker did not claim a batch delivery.");
        using var metadata = JsonDocument.Parse(job!.SafeMetadataJson);
        var eventId = metadata.RootElement.GetProperty("eventId").GetGuid();
        var boardId = metadata.RootElement.GetProperty("boardId").GetGuid();
        Require(await new PostgresWorkEventDeliveryStore(worker).MarkReadyAsync(job, boardId, eventId, ct), "Worker failed batch event readiness.");
        Require(await queue.CompleteAsync(tenant, job.Id, job.LeaseId, workerId, ct), "Worker did not acknowledge batch delivery.");
        var mixed = new[] { seed[0], Change(destination), seed[1], Change(source) };
        await db.ExecuteTenantCommandAsync(tenant, async () => { await events.AppendBatchAsync(mixed, ct); return true; }, x => x, ct);
        await db.ExecuteTenantCommandAsync(tenant, async () => { await events.AppendBatchAsync(mixed, ct); return true; }, x => x, ct);
        Require(await Count($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == total + 2, "Mixed old/new batch repeated or lost an identity.");
        async Task OpposingAsync(bool reverse)
        {
            await using var independent = new PostgresConnectionFactory(apiConnection);
            var journal = new PostgresWorkEventStore(independent, new PostgresBackgroundJobStore(independent));
            await independent.ExecuteTenantCommandAsync(tenant, async () =>
            {
                await journal.AppendBatchAsync(reverse ? [Change(destination), Change(source)] : [Change(source), Change(destination)], ct);
                return true;
            }, x => x, ct);
        }
        await Task.WhenAll(OpposingAsync(false), OpposingAsync(true));
        Require(await Count($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == total + 6, "Opposing publication lost an event.");
        Require(await Count($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}';") == total + 6, "Opposing publication lost a delivery job.");
        Require(await Count($"SELECT count(*) FROM (SELECT board_id FROM work_events WHERE tenant_id='{tenant}' GROUP BY board_id HAVING count(*)=max(sequence) AND min(sequence)=1 AND count(DISTINCT sequence)=count(*)) x;") == 2, "Opposing publication broke contiguous stream allocation.");
        Console.WriteLine($"Work event batch: {total + 6} events/jobs, duplicate/mixed/conflict/tenant refusals, late job rollback, opposing contiguous streams and restricted Worker delivery passed.");
    }
}
