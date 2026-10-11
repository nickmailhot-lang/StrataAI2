using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Application.BackgroundJobs;

// Real restricted SQL/owning transactions; session admission is synthetic here.
internal static class OrganizationOperationalLifecycleContract
{
    private sealed class Admission : ICommandActorAuthorization
    {
        public bool Active { get; set; } = true;
        public Task<bool> VerifyAsync(Guid actor, CancellationToken cancellationToken = default) => Task.FromResult(Active);
    }
    private static void Require(bool condition, string rule) { if (!condition) throw new InvalidOperationException(rule); }
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var organization = Guid.NewGuid(); var other = Guid.NewGuid(); var actor = Guid.NewGuid(); var stranger = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
            VALUES(@actor,@email,upper(@email),'Lifecycle original owner','ACTIVE','unusable-ci-hash',true,now(),now()),
                  (@stranger,@otheremail,upper(@otheremail),'Lifecycle ordinary member','ACTIVE','unusable-ci-hash',true,now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,organization_type,created_at,updated_at)
            VALUES(@tenant,'Retained lifecycle contract',@actor,'ACTIVE',1,'STRATA',now(),now()),
                  (@other,'Other lifecycle contract',@actor,'ACTIVE',1,'STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            VALUES(@member,@tenant,@actor,'OWNER','ACTIVE',now(),now()),
                  (@othermember,@tenant,@stranger,'MEMBER','ACTIVE',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("stranger", stranger);
            seed.Parameters.AddWithValue("email", $"lifecycle-{actor:N}@example.test"); seed.Parameters.AddWithValue("otheremail", $"lifecycle-{stranger:N}@example.test");
            seed.Parameters.AddWithValue("tenant", organization); seed.Parameters.AddWithValue("other", other);
            seed.Parameters.AddWithValue("member", Guid.NewGuid()); seed.Parameters.AddWithValue("othermember", Guid.NewGuid());
            await seed.ExecuteNonQueryAsync(ct);
        }
        var admission = new Admission(); var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ICommandActorAuthorization>(admission);
        services.AddSingleton(new IdentityPolicy(true, false, 12, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<OrganizationOperationalLifecycleService>();
        var store = provider.GetRequiredService<IOrganizationOperationalLifecycleStore>();
        var unit = provider.GetRequiredService<IOrganizationOperationalLifecycleUnitOfWork>();
        var key = Guid.NewGuid();
        var suspended = await service.ChangeAsync(organization, actor, OrganizationStatus.Suspended, 1, key, "restricted-suspension", ct);
        Require(suspended.Succeeded && suspended.Value!.Version == 2, "Restricted lifecycle suspension failed.");
        var review = await service.ReviewAsync(organization, actor, ct);
        Require(review.Succeeded && review.Value!.Status == OrganizationStatus.Suspended && review.Value.Version == 2,
            "Restricted lifecycle current administrative review failed for a suspended parent.");
        var directory = provider.GetRequiredService<OrganizationOperationalLifecycleDirectoryService>();
        var discovered = await directory.ReadAsync(actor, null, ct);
        Require(discovered.Succeeded && discovered.Value!.Items.Any(item => item.OrganizationId == organization
            && item.Status == OrganizationStatus.Suspended && item.Name == "Retained lifecycle contract")
            && discovered.Value.Items.All(item => item.OrganizationId != other), "Restricted inactive administrative discovery lost scope.");
        var memberDirectory = await directory.ReadAsync(stranger, null, ct);
        Require(memberDirectory.Succeeded && memberDirectory.Value!.Items.Count == 0, "Restricted lifecycle directory disclosed an ordinary member target.");
        var replay = provider.GetRequiredService<OrganizationOperationalLifecycleReplayService>();
        var pendingReplay = await replay.ReadAsync(organization, stranger, 0, ct);
        Require(pendingReplay.Succeeded && pendingReplay.Value!.Pending && pendingReplay.Value.Events.Count == 0
            && pendingReplay.Value.Status == OrganizationStatus.Suspended, "Restricted current member replay skipped unpublished suspension.");
        Require((await replay.ReadAsync(other, actor, 0, ct)).ErrorCode == "organization_not_found", "Foreign lifecycle replay was admitted.");
        Require((await service.ReviewAsync(organization, stranger, ct)).ErrorCode == "organization_not_found"
            && (await service.ReviewAsync(other, actor, ct)).ErrorCode == "organization_not_found",
            "Restricted lifecycle review admitted a member or foreign tenant.");
        var recovered = await service.ChangeAsync(organization, actor, OrganizationStatus.Suspended, 1, key, "retry", ct);
        Require(recovered.Succeeded && recovered.Value == suspended.Value, "Restricted lifecycle original receipt changed.");
        Require((await service.ChangeAsync(organization, stranger, OrganizationStatus.Active, 2, Guid.NewGuid(), "member-denied", ct)).ErrorCode == "organization_not_found",
            "Ordinary member changed retained lifecycle state.");
        Require((await service.ChangeAsync(other, actor, OrganizationStatus.Suspended, 1, Guid.NewGuid(), "foreign-denied", ct)).ErrorCode == "organization_not_found",
            "Foreign lifecycle scope disclosed a tenant.");
        var normal = provider.GetRequiredService<IOrganizationUnitOfWork>();
        var forbidden = await normal.ExecuteAsync<bool>(organization, actor, null, false, () => Task.FromResult(OrganizationOperation<bool>.Success(true)), ct);
        Require(forbidden.ErrorCode == "organization_not_found", "Suspended parent admitted normal work.");
        var reactivated = await service.ChangeAsync(organization, actor, OrganizationStatus.Active, 2, Guid.NewGuid(), "restricted-reactivation", ct);
        Require(reactivated.Succeeded && reactivated.Value!.Version == 3, "Restricted lifecycle reactivation failed.");
        var retryAfter = await service.ChangeAsync(organization, actor, OrganizationStatus.Suspended, 1, key, "old-original", ct);
        Require(retryAfter.Succeeded && retryAfter.Value == suspended.Value, "Original lifecycle recovery restored a later state.");
        await using (var proof = new NpgsqlCommand("""
            SELECT o.status='ACTIVE' AND o.version=3 AND o.operational_lifecycle_version=3
             AND (SELECT count(*) FROM organization_operational_lifecycle_history WHERE tenant_id=o.id)=2
             AND (SELECT count(*) FROM organization_operational_lifecycle_receipts WHERE tenant_id=o.id)=2
             AND (SELECT count(*) FROM organization_operational_lifecycle_events WHERE tenant_id=o.id)=2
             AND EXISTS(SELECT 1 FROM audit_events WHERE id=@event AND tenant_id=o.id AND event_type='ORGANIZATION_SUSPENDED')
            FROM organizations o WHERE id=@tenant;
            """, admin))
        {
            proof.Parameters.AddWithValue("tenant", organization); proof.Parameters.AddWithValue("event", suspended.Value!.EventId);
            Require(await proof.ExecuteScalarAsync(ct) is true, "Lifecycle state/source/receipt/audit/event proof differed.");
        }
        foreach (var withdrawSession in new[] { false, true })
        {
            var rollbackKey = Guid.NewGuid();
            var rolledBack = await unit.ExecuteAsync<bool>(organization, actor, async () =>
            {
                var scope = (await store.ReadScopeAsync(organization, actor, ct))!;
                var at = DateTimeOffset.UtcNow; at = at.AddTicks(-(at.Ticks % 10));
                var source = new OrganizationOperationalLifecycleTransition(organization, Guid.NewGuid(), actor, scope.ActorLabel,
                    "ORGANIZATION_OWNER", scope.MembershipId, scope.MembershipVersion, scope.Status, OrganizationStatus.Deactivated,
                    scope.Version + 1, "restricted-rollback", at);
                var receipt = new OrganizationOperationalLifecycleReceipt(actor, rollbackKey,
                    OrganizationOperationalLifecycleRules.Fingerprint(organization, source.Status, scope.Version), source, at.AddHours(24));
                Require(await store.WriteAsync(scope.Version, source, receipt, ct) == OrganizationOperationalLifecycleWriteResult.Written,
                    "Restricted lifecycle rollback setup failed.");
                if (!withdrawSession) return OrganizationOperation<bool>.Failure("injected_after_source_failure");
                admission.Active = false;
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(rolledBack.ErrorCode == (withdrawSession ? "session_unavailable" : "injected_after_source_failure"),
                "Lifecycle owning transaction committed after failure or session withdrawal.");
            admission.Active = true;
            var checkedRollback = await unit.ExecuteAsync<bool>(organization, actor, async () =>
            {
                Require((await store.ReadScopeAsync(organization, actor, ct))!.Version == 3, "Lifecycle rollback changed aggregate state.");
                Require(await store.ReadReceiptAsync(organization, actor, rollbackKey, ct) is null, "Lifecycle rollback retained a receipt.");
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(checkedRollback.Succeeded, "Restricted lifecycle rollback proof could not read scope.");
        }
        admission.Active = false;
        Require((await service.ChangeAsync(organization, actor, OrganizationStatus.Deactivated, 3, Guid.NewGuid(), "withdrawn-session", ct)).ErrorCode == "session_unavailable",
            "Withdrawn lifecycle session committed a change."); admission.Active = true;
        // A direct status/pointer write without its matching immutable source must fail at commit.
        await using var api = new NpgsqlConnection(apiConnection); await api.OpenAsync(ct);
        await using (var isolation = await api.BeginTransactionAsync(ct))
        {
            await using var hidden = new NpgsqlCommand("""
                SELECT set_config('app.tenant_id',@other::text,true);
                SELECT (SELECT count(*) FROM organization_operational_lifecycle_history WHERE tenant_id=@tenant)
                     + (SELECT count(*) FROM organization_operational_lifecycle_receipts WHERE tenant_id=@tenant)
                     + (SELECT count(*) FROM organization_operational_lifecycle_events WHERE tenant_id=@tenant);
                """, api, isolation);
            hidden.Parameters.AddWithValue("other", other); hidden.Parameters.AddWithValue("tenant", organization);
            await using var rows = await hidden.ExecuteReaderAsync(ct);
            Require(await rows.NextResultAsync(ct) && await rows.ReadAsync(ct) && rows.GetInt64(0) == 0,
                "Restricted lifecycle RLS exposed foreign history, receipts or events.");
        }
        await using (var transaction = await api.BeginTransactionAsync(ct))
        {
            await using var raw = new NpgsqlCommand("""
                SELECT set_config('app.tenant_id',@tenant::text,true);
                UPDATE organizations SET status='SUSPENDED',version=4,operational_lifecycle_version=4,updated_at=clock_timestamp() WHERE id=@tenant;
                """, api, transaction); raw.Parameters.AddWithValue("tenant", organization);
            var rejected = false;
            try { await raw.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct); }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation) { rejected = true; }
            Require(rejected, "Status-only lifecycle change committed without a source.");
        }
        await VerifyDeliveryAsync(admin, apiConnection, workerConnection, organization, actor, stranger, replay, ct);
        Console.WriteLine("Restricted Organization lifecycle transitions, original receipts, administrative refusal, rollback and source-only commit guards passed.");
    }

    private static async Task VerifyDeliveryAsync(NpgsqlConnection admin, string apiConnection, string workerConnection,
        Guid organization, Guid actor, Guid member, OrganizationOperationalLifecycleReplayService replay, CancellationToken ct)
    {
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var scopes = new PostgresOrganizationOperationalLifecycleScopeReader(worker);
        Require((await scopes.ReadAsync(null, 100, ct)).Contains(organization), "Operational lifecycle scope was not discovered.");
        var jobs = new PostgresBackgroundJobStore(worker, operationalLifecycleJobsOnly: true);
        var delivery = new PostgresOrganizationOperationalLifecycleDeliveryStore(worker);
        var workerId = Guid.NewGuid();
        await using (var unrelated = new NpgsqlCommand("""
            INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
            VALUES(@job,@tenant,'UNRELATED_PROVIDER','operational-lifecycle-unrelated',@actor,'unrelated-provider','unrelated-contract','{}');
            """, admin))
        {
            unrelated.Parameters.AddWithValue("job", Guid.NewGuid()); unrelated.Parameters.AddWithValue("tenant", organization);
            unrelated.Parameters.AddWithValue("actor", actor); await unrelated.ExecuteNonQueryAsync(ct);
        }
        var claim = await jobs.ClaimAsync(organization, workerId, ct);
        Require(claim is not null && claim.JobType == OrganizationOperationalLifecycleDeliveryHandler.Type,
            "Restricted Worker could not claim an operational lifecycle job.");
        var eventId = OrganizationLifecycleDeliveryHandler.ParseEventId(claim!.SafeMetadataJson);
        Require(!await delivery.MarkReadyAsync(claim with { LeaseId = Guid.NewGuid() }, eventId, ct), "Foreign lease delivered a lifecycle event.");
        Require(!await delivery.MarkReadyAsync(claim with { ActorId = Guid.NewGuid() }, eventId, ct), "Foreign actor delivered a lifecycle event.");
        Require(!await delivery.MarkReadyAsync(claim with { OrganizationId = Guid.NewGuid() }, eventId, ct), "Foreign tenant delivered a lifecycle event.");
        Require(!await delivery.MarkReadyAsync(claim, Guid.NewGuid(), ct), "Foreign event reference delivered a lifecycle event.");
        await using (var expire = new NpgsqlCommand("""
            UPDATE background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second',
                updated_at=clock_timestamp(),version=version+1 WHERE tenant_id=@tenant AND id=@job;
            """, admin))
        { expire.Parameters.AddWithValue("tenant", organization); expire.Parameters.AddWithValue("job", claim.Id); await expire.ExecuteNonQueryAsync(ct); }
        Require(!await delivery.MarkReadyAsync(claim, eventId, ct), "Expired lifecycle lease delivered an event.");
        Require((await scopes.ReadAsync(null, 100, ct)).Contains(organization), "Expired lifecycle lease was not rediscovered.");
        var recovered = await jobs.ClaimAsync(organization, workerId, ct);
        Require(recovered is not null && recovered.Id == claim.Id && recovered.LeaseId != claim.LeaseId
            && recovered.AttemptCount == claim.AttemptCount + 1, "Lifecycle lease recovery did not preserve its original job.");
        Require(!await delivery.MarkReadyAsync(claim, eventId, ct), "Reclaimed lifecycle job accepted its previous lease.");
        claim = recovered!;
        // Committed facts survive a later parent revision and actor withdrawal.
        await using (var withdraw = new NpgsqlCommand("UPDATE users SET status='DEACTIVATED' WHERE id=@actor", admin))
        { withdraw.Parameters.AddWithValue("actor", actor); await withdraw.ExecuteNonQueryAsync(ct); }
        try
        {
            await new OrganizationOperationalLifecycleDeliveryHandler(delivery).ExecuteAsync(claim, ct);
            var first = await ReadyAtAsync();
            Require(first is not null, "Operational lifecycle event did not become ready.");
            Require(await delivery.MarkReadyAsync(claim, eventId, ct) && await ReadyAtAsync() == first,
                "Lifecycle redelivery moved its original readiness clock.");
            var prefix = await replay.ReadAsync(organization, member, 0, ct);
            Require(prefix.Succeeded && prefix.Value!.Events.Count == 1 && prefix.Value.Events[0].EventId == eventId
                && prefix.Value.Pending && prefix.Value.NextVersion == 2, "Restricted replay skipped an unpublished later source.");
        }
        finally
        {
            await using var restore = new NpgsqlCommand("UPDATE users SET status='ACTIVE' WHERE id=@actor", admin);
            restore.Parameters.AddWithValue("actor", actor); await restore.ExecuteNonQueryAsync(ct);
        }
        Require(await jobs.CompleteAsync(organization, claim.Id, claim.LeaseId, workerId, ct), "Lifecycle job completion failed.");
        Require(!await delivery.MarkReadyAsync(claim, eventId, ct), "Completed lease remained a lifecycle delivery capability.");
        var second = await jobs.ClaimAsync(organization, workerId, ct);
        Require(second is not null, "Reactivation delivery was missing.");
        await new OrganizationOperationalLifecycleDeliveryHandler(delivery).ExecuteAsync(second!, ct);
        Require(await jobs.CompleteAsync(organization, second!.Id, second.LeaseId, workerId, ct), "Reactivation job completion failed.");
        Require(await jobs.ClaimAsync(organization, workerId, ct) is null, "Rolled-back lifecycle publication left a queued job.");
        await using (var unrelated = new NpgsqlCommand("""
            SELECT count(*)=1 AND bool_and(state='PENDING' AND attempt_count=0)
            FROM background_jobs WHERE tenant_id=@tenant AND job_type='UNRELATED_PROVIDER';
            """, admin))
        { unrelated.Parameters.AddWithValue("tenant", organization); Require(await unrelated.ExecuteScalarAsync(ct) is true, "Typed lifecycle dispatcher touched an unrelated provider job."); }
        Require(!(await scopes.ReadAsync(null, 100, ct)).Contains(organization), "Completed lifecycle scope stayed eligible.");
        var publishedReplay = await replay.ReadAsync(organization, member, 0, ct);
        Require(publishedReplay.Succeeded && publishedReplay.Value!.Events.Count == 2 && !publishedReplay.Value.Pending,
            "Restricted member could not recover both published lifecycle facts.");
        var resumedReplay = await replay.ReadAsync(organization, member, 2, ct);
        Require(resumedReplay.Succeeded && resumedReplay.Value!.Events.Count == 1 && resumedReplay.Value.Events[0].Version == 3,
            "Restricted lifecycle replay continuation repeated or skipped its later fact.");
        await using (var proof = new NpgsqlCommand("""
            SELECT count(*)=2 AND bool_and(ready_at IS NOT NULL AND ready_at>=created_at AND updated_at=ready_at)
            FROM organization_operational_lifecycle_events WHERE tenant_id=@tenant;
            """, admin))
        { proof.Parameters.AddWithValue("tenant", organization); Require(await proof.ExecuteScalarAsync(ct) is true, "Lifecycle delivery clocks or rollback count differed."); }
        await using var apiFactory = new PostgresConnectionFactory(apiConnection);
        await using var apiSession = await apiFactory.OpenTenantSessionAsync(organization, ct);
        await using var denied = new NpgsqlCommand("SELECT public.deliver_organization_operational_lifecycle_event(@tenant,@job,@actor,@worker,@lease,@event)", apiSession.Connection, apiSession.Transaction);
        denied.Parameters.AddWithValue("tenant", organization); denied.Parameters.AddWithValue("job", claim.Id);
        denied.Parameters.AddWithValue("actor", actor); denied.Parameters.AddWithValue("worker", workerId);
        denied.Parameters.AddWithValue("lease", claim.LeaseId); denied.Parameters.AddWithValue("event", eventId);
        var refused = false;
        try { await denied.ExecuteScalarAsync(ct); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { refused = true; }
        Require(refused, "API runtime borrowed the Worker lifecycle delivery capability.");
        Console.WriteLine("Restricted operational lifecycle publication, typed discovery/claims, lease/source refusal, original-clock redelivery and API capability denial passed.");

        async Task<DateTimeOffset?> ReadyAtAsync()
        {
            await using var query = new NpgsqlCommand("SELECT ready_at FROM organization_operational_lifecycle_events WHERE tenant_id=@tenant AND event_id=@event", admin);
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("event", eventId);
            var value = await query.ExecuteScalarAsync(ct);
            return value is DateTime timestamp ? new DateTimeOffset(timestamp) : null;
        }
    }
}
