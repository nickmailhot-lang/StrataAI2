using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Persistence;

// PRD-04/60: actual restricted subject writes and canonical Work publication,
// followed by concurrent restricted delivery. Account/initial rows are fixtures;
// this does not claim HTTP session admission or retained-image browser evidence.
internal static class InvitationBoardAuthorityConcurrencyContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var board = Guid.NewGuid();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var email = $"BOARD-CONCURRENT-{tenant:N}@EXAMPLE.TEST".ToUpperInvariant();
        var other = $"BOARD-CONCURRENT-OTHER-{tenant:N}@EXAMPLE.TEST".ToUpperInvariant();
        var future = $"BOARD-CONCURRENT-FUTURE-{tenant:N}@EXAMPLE.TEST".ToUpperInvariant();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             VALUES(@actor,lower(@email),@email,'Board authority concurrency fixture','ACTIVE',true,'unused-fixture-hash',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
             VALUES(@tenant,'Board authority concurrency fixture',@actor,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
             VALUES(@board,@tenant,'Before concurrent authority',now(),now());
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             SELECT gen_random_uuid(),@tenant,lower(CASE WHEN n=205 THEN @other ELSE @email END),
              CASE WHEN n=205 THEN @other ELSE @email END,encode(sha256((@tenant::text||'/'||n)::bytea),'hex'),
              'PORTAL','OWNER',@actor,clock_timestamp()-interval '1 second',clock_timestamp()+interval '7 days'
             FROM generate_series(1,205) n;
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             VALUES(gen_random_uuid(),@tenant,lower(@future),@future,encode(sha256(@future::bytea),'hex'),
              'PORTAL','OWNER',@actor,clock_timestamp()+interval '1 day',clock_timestamp()+interval '7 days');
            """, admin))
        {
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("board", board);
            seed.Parameters.AddWithValue("email", email); seed.Parameters.AddWithValue("other", other); seed.Parameters.AddWithValue("future", future);
            await seed.ExecuteNonQueryAsync(ct);
        }
        await using var api = new PostgresConnectionFactory(apiConnection);
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(api); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<PostgresBackgroundJobStore>();
        services.AddSingleton<ICommandActorAuthorization, NoActorFixture>();
        services.AddStrataAiWorkManagement(new(RuntimeMode.Production, "contract", "contract"));
        await using var provider = services.BuildServiceProvider();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var work = provider.GetRequiredService<IWorkManagementStore>();
        var events = provider.GetRequiredService<IWorkEventStore>();
        for (var index = 0; index < 2; index++)
        {
            var result = await unit.ExecuteReadAsync(tenant, null, "fixture_scope", () => Task.FromResult(true), async () => {
                var at = DateTimeOffset.UtcNow;
                var changed = await work.UpdateBoardAsync(board, $"Concurrent authority {index}", null, "COLOR", null, index + 1, at, ct);
                Require(changed is not null && changed.Version == index + 2, "Concurrent fixture failed its actual Board mutation.");
                await events.AppendAsync(new(index == 0 ? first : second, tenant, board, actor, "BOARD_UPDATED", "Board", board,
                    changed!.Version, "board-authority-concurrent", changed.UpdatedAt), ct);
                return WorkOperation<bool>.Success(true);
            }, ct);
            Require(result.Succeeded, "Concurrent fixture failed its owning Board command/source publication.");
        }
        async Task<string> Unrelated()
        {
            await using var query = new NpgsqlCommand("""
                SELECT jsonb_build_object(
                 'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j
                   WHERE tenant_id=@tenant AND job_type<>'INVITATION_RECIPIENT_AUTHORITY_PAGE'),
                 'work',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id=@tenant))::text;
                """, admin);
            query.Parameters.AddWithValue("tenant", tenant); return (string)(await query.ExecuteScalarAsync(ct))!;
        }
        var unrelated = await Unrelated();
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var jobs = new PostgresBackgroundJobStore(worker, authorityJobsOnly: true);
        var handler = new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker));
        var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => jobs.ClaimAsync(tenant, Guid.NewGuid(), ct)));
        Require(claims.All(claim => claim is not null) && claims.Select(claim => claim!.Id).Distinct().Count() == 2,
            "Concurrent authority roots were not independently leased.");
        await Task.WhenAll(claims.Select(async claim => {
            await handler.ExecuteAsync(claim!, ct);
            await handler.ExecuteAsync(claim!, ct); // Committed root replay cannot increment again.
            Require(await jobs.CompleteAsync(tenant, claim!.Id, claim.LeaseId, claim.WorkerId, ct), "Concurrent root acknowledgment failed.");
        }));
        var processor = new BackgroundJobProcessor(jobs, new SystemClock(), [handler]);
        var workers = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var completed = 0;
        while (true)
        {
            var outcomes = await Task.WhenAll(workers.Select(id => processor.ProcessOneAsync(tenant, id, ct)));
            Require(outcomes.All(value => value is JobProcessingResult.Completed or JobProcessingResult.Empty),
                "Concurrent authority continuation failed, retried or lost its lease.");
            completed += outcomes.Count(value => value == JobProcessingResult.Completed);
            Require(completed <= 4, "Concurrent authority publication repeated a continuation.");
            if (outcomes.All(value => value == JobProcessingResult.Empty)) break;
        }
        Require(completed == 4 && await Unrelated() == unrelated, "Concurrent authority lost a page or changed unrelated delivery/readiness.");
        await using var verify = new NpgsqlCommand("""
            SELECT
             (SELECT count(*)=6 AND bool_and(completed_at IS NOT NULL) FROM invitation_recipient_authority_pages WHERE tenant_id=@tenant),
             (SELECT array_agg(scanned_count ORDER BY scanned_count)=ARRAY[5,5,100,100,100,100]
               FROM invitation_recipient_authority_pages WHERE tenant_id=@tenant),
             (SELECT count(*)=6 AND bool_and(j.state='SUCCEEDED' AND j.attempt_count=1 AND j.actor_id=e.actor_id
                 AND j.correlation_id=e.correlation_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id))
              FROM background_jobs j JOIN invitation_recipient_authority_pages p ON p.tenant_id=j.tenant_id AND p.job_id=j.id
              JOIN invitation_recipient_authority_source_rows e ON e.tenant_id=p.tenant_id AND e.event_id=p.source_event_id
              WHERE p.tenant_id=@tenant),
             (SELECT count(*)=4 FROM invitation_recipient_authority_effects WHERE tenant_id=@tenant),
             (SELECT count(*)=2 AND bool_and(revision=2) FROM invitation_recipient_authority_revisions WHERE email_normalized IN (@email,@other)),
             NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized=@future),
             (SELECT count(*)=2 AND bool_and(work_event_id IN (@first,@second) AND metadata_event_id IS NULL)
               FROM invitation_recipient_authority_sources WHERE tenant_id=@tenant);
            """, admin);
        verify.Parameters.AddWithValue("tenant", tenant); verify.Parameters.AddWithValue("email", email); verify.Parameters.AddWithValue("other", other);
        verify.Parameters.AddWithValue("future", future); verify.Parameters.AddWithValue("first", first); verify.Parameters.AddWithValue("second", second);
        await using var row = await verify.ExecuteReaderAsync(ct);
        Require(await row.ReadAsync(ct) && Enumerable.Range(0, 7).All(index => !row.IsDBNull(index) && row.GetBoolean(index)),
            "Concurrent Board delivery lost original sources, bounded pages, recipient increments, cutoff or deduplication.");
        Console.WriteLine("Concurrent Board authority: two restricted mutations/canonical sources, separately leased concurrent roots, 100/100/5 pages each, committed replay, exactly two increments per recipient, future cutoff and unrelated queue/readiness isolation passed.");
    }
}
