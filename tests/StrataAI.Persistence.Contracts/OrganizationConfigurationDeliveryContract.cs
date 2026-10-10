using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

internal static class OrganizationConfigurationDeliveryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
            VALUES(@actor,@email,upper(@email),'Configuration delivery owner','ACTIVE','unusable-ci-hash',true,now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
            VALUES(@tenant,'Configuration delivery contract',@actor,'ACTIVE','STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            VALUES(@member,@tenant,@actor,'OWNER','ACTIVE',now(),now());
            INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
            VALUES(@other_job,@tenant,'UNRELATED_PROVIDER','unrelated-delivery-contract',@actor,'unrelated-provider','unrelated-contract','{}');
            """, admin))
        {
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("actor", actor);
            seed.Parameters.AddWithValue("email", $"config-delivery-{actor:N}@example.test"); seed.Parameters.AddWithValue("member", Guid.NewGuid());
            seed.Parameters.AddWithValue("other_job", Guid.NewGuid()); await seed.ExecuteNonQueryAsync(ct);
        }
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection(); services.AddLogging();
        services.AddSingleton(api); services.AddSingleton<StrataAI.Application.Common.IClock, Clock>();
        services.AddSingleton<StrataAI.Application.Identity.ICommandActorAuthorization, Admission>();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddSingleton(new StrataAI.Application.Identity.IdentityPolicy(true,false,12,TimeSpan.FromHours(1),TimeSpan.FromHours(1)));
        var runtime = new StrataAI.Application.Runtime.RuntimeDescriptor(StrataAI.Application.Runtime.RuntimeMode.Production,"contract","contract");
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var configuration = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(provider);
        var first = await configuration.ChangeAsync(tenant,actor,new("Private delivery legal name","CA-BC","UTC"),0,Guid.NewGuid(),"configuration-delivery-first",ct);
        var second = await configuration.ChangeAsync(tenant,actor,new("Private later legal name","CA-BC","UTC"),1,Guid.NewGuid(),new string('r',256),ct);
        Require(first.Succeeded && second.Succeeded,"Configuration delivery seed did not commit two source revisions.");
        // This fixture hosts only the configuration handler, like the dedicated
        // discovery Worker. Other source jobs must remain for their own handlers.
        foreach (var flags in new[] { (Metadata: true, Authority: false), (Metadata: false, Authority: true) })
        {
            var deniedScope = false;
            try { _ = new PostgresBackgroundJobStore(worker,metadataJobsOnly:flags.Metadata,authorityJobsOnly:flags.Authority,configurationJobsOnly:true); }
            catch (ArgumentException) { deniedScope = true; }
            Require(deniedScope,"Configuration claim scope was combined with another typed dispatcher.");
        }
        var discovered = await new PostgresOrganizationConfigurationScopeReader(worker).ReadAsync(null,100,ct);
        Require(discovered.Contains(tenant),"Configuration discovery omitted committed pending work.");
        var apiDiscoveryDenied = false;
        try { await new PostgresOrganizationConfigurationScopeReader(api).ReadAsync(null,100,ct); }
        catch (PostgresException e) when(e.SqlState==PostgresErrorCodes.InsufficientPrivilege) { apiDiscoveryDenied=true; }
        Require(apiDiscoveryDenied,"API gained global configuration queue discovery.");
        var jobs = new PostgresBackgroundJobStore(worker, configurationJobsOnly: true);
        var claim = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Configuration event claim missing.");
        Require(claim.JobType == OrganizationConfigurationDeliveryHandler.Type, "Configuration fixture claimed the wrong job.");
        var eventId = OrganizationLifecycleDeliveryHandler.ParseEventId(claim.SafeMetadataJson);
        var store = new PostgresOrganizationConfigurationDeliveryStore(worker);
        DateTime? originalCreated = null;
        async Task<DateTime?> ReadyAt()
        {
            await using var query = new NpgsqlCommand("SELECT ready_at,created_at,updated_at FROM organization_configuration_events WHERE tenant_id=@tenant AND event_id=@event", admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("event", eventId);
            await using var reader = await query.ExecuteReaderAsync(ct);
            Require(await reader.ReadAsync(ct), "Configuration delivery source missing.");
            DateTime? ready = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
            var created = reader.GetDateTime(1); var updated = reader.GetDateTime(2);
            originalCreated ??= created;
            Require(created == originalCreated && updated == (ready ?? created),
                "Configuration delivery or rollback changed retained creation/derived update clocks.");
            return ready;
        }
        Require(await ReadyAt() is null, "Configuration event was ready before Worker delivery.");
        foreach (var bad in new[] { claim with { OrganizationId = Guid.NewGuid() }, claim with { ActorId = Guid.NewGuid() },
            claim with { LeaseId = Guid.NewGuid() }, claim with { WorkerId = Guid.NewGuid() } })
            Require(!await store.MarkReadyAsync(bad, eventId, ct), "Configuration tenant/actor/lease/worker fence failed.");
        Require(!await store.MarkReadyAsync(claim, Guid.NewGuid(), ct), "Configuration event identity fence failed.");
        var apiDenied = false;
        try { await new PostgresOrganizationConfigurationDeliveryStore(api).MarkReadyAsync(claim, eventId, ct); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { apiDenied = true; }
        Require(apiDenied && await ReadyAt() is null, "API gained configuration delivery authority.");
        async Task Admin(string sql)
        { await using var command = new NpgsqlCommand(sql, admin); await command.ExecuteNonQueryAsync(ct); }
        await Admin($"""
            CREATE FUNCTION public.ci_configuration_delivery_expiry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.tenant_id='{tenant:D}'::uuid THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;
             END IF; RETURN NEW; END $$;
            CREATE TRIGGER ci_configuration_delivery_expiry AFTER UPDATE OF ready_at ON organization_configuration_events
             FOR EACH ROW EXECUTE FUNCTION public.ci_configuration_delivery_expiry();
            """);
        try
        {
            var expired = false;
            try { await store.MarkReadyAsync(claim, eventId, ct); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation) { expired = true; }
            Require(expired && await ReadyAt() is null, "Late configuration delivery expiry retained readiness.");
        }
        finally { await Admin("DROP TRIGGER ci_configuration_delivery_expiry ON organization_configuration_events; DROP FUNCTION public.ci_configuration_delivery_expiry();"); }
        await new OrganizationConfigurationDeliveryHandler(store).ExecuteAsync(claim, ct);
        var originalReady = await ReadyAt(); Require(originalReady is not null, "Valid configuration delivery did not become ready.");
        await new OrganizationConfigurationDeliveryHandler(store).ExecuteAsync(claim, ct);
        Require(await ReadyAt() == originalReady, "Duplicate delivery rewrote readiness.");
        await Admin($"UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;");
        Require(!await store.MarkReadyAsync(claim, eventId, ct), "Expired duplicate delivery succeeded.");
        var reclaimed = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Configuration delivery was not reclaimed.");
        Require(reclaimed.Id == claim.Id && reclaimed.LeaseId != claim.LeaseId, "Configuration reclaim lost stable job identity.");
        Require(!await store.MarkReadyAsync(claim, eventId, ct), "Superseded delivery claim succeeded.");
        await new OrganizationConfigurationDeliveryHandler(store).ExecuteAsync(reclaimed, ct);
        Require(await ReadyAt() == originalReady && await jobs.CompleteAsync(tenant, reclaimed.Id, reclaimed.LeaseId, reclaimed.WorkerId, ct),
            "Configuration reclaimed delivery lost original readiness or acknowledgment.");
        foreach (var factory in new[] { api, worker })
        {
            await using var session = await factory.OpenTenantSessionAsync(tenant, ct);
            await using var query = new NpgsqlCommand("UPDATE organization_configuration_events SET ready_at=clock_timestamp() WHERE tenant_id=@tenant", session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("event", eventId); var denied = false;
            try { await query.ExecuteNonQueryAsync(ct); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { denied = true; }
            Require(denied, "Runtime acquired direct configuration event mutation.");
        }
        await using (var session = await worker.OpenTenantSessionAsync(tenant,ct))
        {
            await using var query = new NpgsqlCommand("SELECT record_json FROM organization_configuration_history WHERE tenant_id=@tenant",session.Connection,session.Transaction);
            query.Parameters.AddWithValue("tenant",tenant); var privateDenied=false;
            try { await query.ExecuteNonQueryAsync(ct); }
            catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.InsufficientPrivilege) { privateDenied=true; }
            Require(privateDenied,"Configuration Worker gained private revision reads.");
        }
        Require(actor == claim.ActorId, "Configuration delivery changed retained actor.");
        // A committed event is historical evidence, not a new action by its
        // actor. Departure cannot strand the remaining committed update.
        await Admin($"UPDATE organization_members SET status='REMOVED',version=version+1 WHERE tenant_id='{tenant:D}'::uuid AND user_id='{actor:D}'::uuid;");
        var next = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Committed update delivery was missing.");
        var nextEvent = OrganizationLifecycleDeliveryHandler.ParseEventId(next.SafeMetadataJson);
        Require(next.JobType == OrganizationConfigurationDeliveryHandler.Type && nextEvent != eventId,
            "Committed update delivery reused the prior event.");
        await new OrganizationConfigurationDeliveryHandler(store).ExecuteAsync(next, ct);
        Require(await jobs.CompleteAsync(tenant, next.Id, next.LeaseId, next.WorkerId, ct),
            "Committed update delivery was stranded by actor departure.");
        Require(await jobs.ClaimAsync(tenant,Guid.NewGuid(),ct) is null,"Configuration dispatcher claimed an unrelated provider job.");
        Require(!(await new PostgresOrganizationConfigurationScopeReader(worker).ReadAsync(null,100,ct)).Contains(tenant),
            "Configuration discovery retained completed delivery work.");
        await using (var untouched = new NpgsqlCommand("SELECT state='PENDING' AND attempt_count=0 FROM background_jobs WHERE tenant_id=@tenant AND job_type='UNRELATED_PROVIDER'",admin))
        { untouched.Parameters.AddWithValue("tenant",tenant); Require(await untouched.ExecuteScalarAsync(ct) is true,"Configuration dispatcher retired unrelated provider work."); }
        Console.WriteLine("Organization configuration delivery: restricted claim fences, late readiness rollback, duplicate/reclaimed delivery and disabled direct mutation passed.");
    }
    private sealed class Admission : StrataAI.Application.Identity.ICommandActorAuthorization
    { public Task<bool> VerifyAsync(Guid actorId,CancellationToken cancellationToken=default)=>Task.FromResult(true); }
    private sealed class Clock : StrataAI.Application.Common.IClock
    { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
