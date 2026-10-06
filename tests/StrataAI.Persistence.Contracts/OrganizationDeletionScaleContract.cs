using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// PRD-03 WS-FR-010 / TC-13 and PRD-18: actual bounded graph mutation and
// delivery at the required scale, rather than admin-staged reference traversal.
// Initial data and account admission are fixtures; this is not HTTP, object
// provider erasure, browser acceptance or a server acknowledgment benchmark.
internal static class OrganizationDeletionScaleContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var request = Guid.NewGuid();
        const int activeCards = 5000, archivedCards = 100000, lists = 200;
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Deletion scale fixture','ACTIVE','unused-contract-hash',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Deletion scale fixture',@actor,'ACTIVE',1,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
             VALUES(@tenant,@tenant,'Scale Board',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,archived_at,created_at,updated_at)
             SELECT md5(@tenant::text||':list:'||n)::uuid,@tenant,@tenant,'Scale List','500000000000000000000000000000',
              CASE WHEN n%2=0 THEN 'ARCHIVED' ELSE 'ACTIVE' END,CASE WHEN n%2=0 THEN now() END,now(),now()
             FROM generate_series(1,200) n;
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,created_at,updated_at)
             SELECT md5(@tenant::text||':card:'||n)::uuid,@tenant,@tenant,md5(@tenant::text||':list:'||(1+n%200))::uuid,
              'Scale Card','500000000000000000000000000000',CASE WHEN n<=5000 THEN 'ACTIVE' ELSE 'ARCHIVED' END,
              CASE WHEN n>5000 THEN now() END,now(),now() FROM generate_series(1,105000) n;
            CREATE TEMP TABLE deletion_scale_card_history AS
             SELECT id,version,archived_at,created_at FROM cards WHERE tenant_id=@tenant;
            CREATE TEMP TABLE deletion_scale_list_history AS
             SELECT id,version,archived_at,created_at FROM board_lists WHERE tenant_id=@tenant;
            """, admin))
        {
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("actor", actor);
            seed.Parameters.AddWithValue("email", $"deletion-scale-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<ICommandActorAuthorization, AdmissionFixture>();
        services.AddStrataAiOrganizations(new(RuntimeMode.Production, "contract", "contract"));
        await using var provider = services.BuildServiceProvider();
        var unit = provider.GetRequiredService<IOrganizationUnitOfWork>();
        var publisher = new PostgresOrganizationDeletionJobPublisher(provider.GetRequiredService<PostgresConnectionFactory>(),
            new PostgresBackgroundJobStore(provider.GetRequiredService<PostgresConnectionFactory>()));
        var accepted = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
        {
            await using var scope = await provider.GetRequiredService<PostgresConnectionFactory>().OpenTenantSessionAsync(tenant, ct);
            await using var change = new NpgsqlCommand("UPDATE organizations SET status='DELETING',version=2,updated_at=clock_timestamp() WHERE id=@tenant AND status='ACTIVE' AND version=1", scope.Connection, scope.Transaction);
            change.Parameters.AddWithValue("tenant", tenant);
            Require(await change.ExecuteNonQueryAsync(ct) == 1, "Scale parent transition failed.");
            Require(await publisher.PublishAsync(tenant, actor, request, 2, "deletion-scale", ct), "Scale accepted publication failed.");
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Require(accepted.Succeeded, "Scale owning command was refused.");

        await using var worker = new PostgresConnectionFactory(workerConnection);
        var diagnostics = new ScaleDiagnostics();
        var processor = new BackgroundJobProcessor(new PostgresBackgroundJobStore(worker), new SystemClock(), [
            new OrganizationDeletionPageHandler(new PostgresOrganizationDeletionPageStore(worker)),
            new WorkEventDeliveryHandler(new PostgresWorkEventDeliveryStore(worker)),
            new OrganizationLifecycleDeliveryHandler(new PostgresOrganizationLifecycleDeliveryStore(worker))
        ], diagnostics);
        // Four real Worker identities compete through the normal SKIP LOCKED
        // claim. No queue/checkpoint/lease is edited to select or accelerate work.
        var identities = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var watch = Stopwatch.StartNew(); var processed = 0; var nextReport = 4096;
        while (true)
        {
            var results = await Task.WhenAll(identities.Select(id => processor.ProcessOneAsync(tenant, id, ct)));
            Require(results.All(result => result is JobProcessingResult.Completed or JobProcessingResult.Empty),
                "Scale Worker encountered retry, failure or lost lease.");
            processed += results.Count(result => result == JobProcessingResult.Completed);
            Require(processed <= 110000, "Scale continuation or event publication did not converge.");
            if (processed >= nextReport)
            {
                Console.WriteLine($"Deletion mutation scale progress: {processed} actual completed jobs, elapsed {watch.ElapsedMilliseconds}ms.");
                nextReport += 4096;
            }
            if (results.All(result => result == JobProcessingResult.Empty)) break;
        }
        watch.Stop();
        await using (var verify = new NpgsqlCommand("""
            SELECT o.status,o.version,p.phase,
             (SELECT count(*) FROM cards WHERE tenant_id=@tenant),
             (SELECT count(*) FROM board_lists WHERE tenant_id=@tenant),
             NOT EXISTS(SELECT 1 FROM cards c JOIN deletion_scale_card_history h USING(id)
              WHERE c.tenant_id=@tenant AND (c.lifecycle_state<>'DELETED' OR c.deleted_by IS DISTINCT FROM @actor OR c.deleted_at IS NULL
               OR c.version<>h.version+1 OR c.archived_at IS DISTINCT FROM h.archived_at OR c.created_at<>h.created_at)),
             NOT EXISTS(SELECT 1 FROM board_lists l JOIN deletion_scale_list_history h USING(id)
              WHERE l.tenant_id=@tenant AND (l.lifecycle_state<>'DELETED' OR l.deleted_by IS DISTINCT FROM @actor OR l.deleted_at IS NULL
               OR l.version<>h.version+1 OR l.archived_at IS DISTINCT FROM h.archived_at OR l.created_at<>h.created_at)),
             (SELECT count(*) FROM organization_deletion_steps WHERE tenant_id=@tenant AND (candidate_count>128 OR candidate_count<0)),
             (SELECT sum(candidate_count) FROM organization_deletion_steps WHERE tenant_id=@tenant AND phase='CARDS'),
             (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant AND event_type='CARD_DELETED'),
             (SELECT count(DISTINCT entity_id) FROM audit_events WHERE tenant_id=@tenant AND event_type='CARD_DELETED'),
             (SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND event_type='CARD_DELETED' AND ready_at IS NOT NULL),
             NOT EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id=@tenant AND (state<>'SUCCEEDED' OR attempt_count<>1)),
             (SELECT count(*) FROM organization_lifecycle_events WHERE tenant_id=@tenant AND ready_at IS NOT NULL
              AND event_type='ORGANIZATION_DELETED' AND actor_id=@actor AND entity_version=3),
             NOT EXISTS(SELECT 1 FROM work_events WHERE tenant_id=@tenant AND actor_id<>@actor),
             (SELECT count(*) FROM boards WHERE tenant_id=@tenant AND lifecycle_state='DELETED' AND deleted_by=@actor AND version=2),
             (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant AND event_type='LIST_DELETED'),
             (SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND event_type='LIST_DELETED' AND ready_at IS NOT NULL),
             (SELECT count(DISTINCT entity_id) FROM work_events WHERE tenant_id=@tenant AND event_type='CARD_DELETED' AND ready_at IS NOT NULL)
            FROM organizations o JOIN organization_deletion_progress p ON p.tenant_id=o.id WHERE o.id=@tenant;
            """, admin))
        {
            verify.Parameters.AddWithValue("tenant", tenant); verify.Parameters.AddWithValue("actor", actor);
            await using var row = await verify.ExecuteReaderAsync(ct);
            Require(await row.ReadAsync(ct) && row.GetString(0) == "DELETED" && row.GetInt64(1) == 3 && row.GetString(2) == "COMPLETE"
                && row.GetInt64(3) == activeCards + archivedCards && row.GetInt64(4) == lists
                && row.GetBoolean(5) && row.GetBoolean(6) && row.GetInt64(7) == 0
                && row.GetInt64(8) == activeCards + archivedCards && row.GetInt64(9) == activeCards + archivedCards
                && row.GetInt64(10) == activeCards + archivedCards && row.GetInt64(11) == activeCards + archivedCards
                && row.GetBoolean(12) && row.GetInt64(13) == 1 && row.GetBoolean(14) && row.GetInt64(15) == 1
                && row.GetInt64(16) == lists && row.GetInt64(17) == lists && row.GetInt64(18) == activeCards + archivedCards,
                "Actual scale deletion lost graph rows, duplicated effects, archive history, attribution, page bounds or ready completion.");
        }
        var observation = new PostgresOrganizationDeletionObservationReader(provider.GetRequiredService<PostgresConnectionFactory>(),
            new AdmissionFixture(), NullLogger<PostgresOrganizationDeletionObservationReader>.Instance);
        var terminal = await observation.ReadAsync(tenant, actor, request, ct);
        Require(terminal.Succeeded && terminal.Value is { State: "COMPLETED", Version: 3, EventId: not null, CompletedAt: not null },
            "Scale completion was not recoverable through the restricted Owner reader.");
        Require((await observation.ReadAsync(tenant, actor, request, ct)).Value == terminal.Value,
            "Scale completion recovery changed the original event, time or revision.");
        await OrganizationLifecycleReplayContract.RunAsync(admin, apiConnection, tenant, actor, terminal.Value!.EventId!.Value, true, ct);
        Require(diagnostics.Pages > 800 && diagnostics.Deliveries == activeCards + archivedCards + lists + 1
            && diagnostics.Terminals == 1, "Scale Worker did not execute all bounded mutations and actual event delivery jobs.");
        Console.WriteLine($"Deletion mutation scale: {activeCards} active + {archivedCards} archived Cards, {lists} Lists, {diagnostics.Pages} bounded mutation jobs, {diagnostics.Deliveries} ready work events, one ready terminal; elapsed {watch.ElapsedMilliseconds}ms, maximum leased page {diagnostics.MaximumPageMilliseconds}ms.");
        await using var cleanup = new NpgsqlCommand("DROP TABLE deletion_scale_card_history,deletion_scale_list_history", admin);
        await cleanup.ExecuteNonQueryAsync(ct);
    }

    private sealed class AdmissionFixture : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(actorId != Guid.Empty); }
    }
    private sealed class ScaleDiagnostics : IBackgroundJobDiagnostics
    {
        private int _pages, _deliveries, _terminals; private long _maximumPageMilliseconds;
        public int Pages => _pages; public int Deliveries => _deliveries; public int Terminals => _terminals;
        public long MaximumPageMilliseconds => _maximumPageMilliseconds;
        public void Record(ClaimedBackgroundJob job, JobProcessingResult outcome)
        {
            if (outcome != JobProcessingResult.Completed) throw new InvalidOperationException("Scale job did not complete.");
            if (job.JobType == OrganizationDeletionJobs.Type)
            {
                Interlocked.Increment(ref _pages);
                // Claims use the normal two-minute lease. The elapsed value also
                // includes job acknowledgment and is read only after completion.
                var elapsed = Math.Max(0, (long)(DateTimeOffset.UtcNow - job.LeaseExpiresAt.AddMinutes(-2)).TotalMilliseconds);
                long previous;
                do { previous = Interlocked.Read(ref _maximumPageMilliseconds); if (elapsed <= previous) break; }
                while (Interlocked.CompareExchange(ref _maximumPageMilliseconds, elapsed, previous) != previous);
            }
            else if (job.JobType == "WORK_EVENT_READY") Interlocked.Increment(ref _deliveries);
            else if (job.JobType == "ORGANIZATION_LIFECYCLE_EVENT_READY") Interlocked.Increment(ref _terminals);
            else throw new InvalidOperationException("Unexpected scale job type.");
        }
    }
}
