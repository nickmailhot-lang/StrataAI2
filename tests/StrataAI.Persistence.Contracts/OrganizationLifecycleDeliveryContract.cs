using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

internal static class OrganizationLifecycleDeliveryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, Guid tenant, Guid actor, CancellationToken ct)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var jobs = new PostgresBackgroundJobStore(worker);
        // Drain the genuine terminal authority source through its typed leased
        // handler before this fixture isolates lifecycle readiness/reclaim behavior.
        var authorityJobs = new PostgresBackgroundJobStore(worker, authorityJobsOnly:true);
        var authorityClaim = await authorityJobs.ClaimAsync(tenant,Guid.NewGuid(),ct)
            ?? throw new InvalidOperationException("Terminal authority source claim missing.");
        await new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker)).ExecuteAsync(authorityClaim,ct);
        Require(await authorityJobs.CompleteAsync(tenant,authorityClaim.Id,authorityClaim.LeaseId,authorityClaim.WorkerId,ct),
            "Terminal authority source acknowledgment failed.");
        Require(await authorityJobs.ClaimAsync(tenant,Guid.NewGuid(),ct) is null,
            "Empty terminal fixture published additional authority pages.");
        var claim = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Lifecycle event claim missing.");
        Require(claim.JobType == OrganizationLifecycleDeliveryHandler.Type, "Lifecycle fixture claimed the wrong job.");
        var eventId = OrganizationLifecycleDeliveryHandler.ParseEventId(claim.SafeMetadataJson);
        var store = new PostgresOrganizationLifecycleDeliveryStore(worker);
        DateTime? originalCreated = null;
        async Task<DateTime?> ReadyAt()
        {
            await using var query = new NpgsqlCommand("SELECT ready_at,created_at,updated_at FROM organization_lifecycle_events WHERE tenant_id=@tenant", admin);
            query.Parameters.AddWithValue("tenant", tenant);
            await using var reader = await query.ExecuteReaderAsync(ct);
            Require(await reader.ReadAsync(ct), "Lifecycle delivery source missing.");
            DateTime? ready = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
            var created = reader.GetDateTime(1); var updated = reader.GetDateTime(2);
            originalCreated ??= created;
            Require(created == originalCreated && updated == (ready ?? created),
                "Lifecycle delivery or rollback changed retained creation/derived update clocks.");
            return ready;
        }
        Require(await ReadyAt() is null, "Lifecycle event was ready before Worker delivery.");
        await OrganizationLifecycleReplayContract.RunAsync(admin, apiConnection, tenant, actor, eventId, false, ct);
        foreach (var bad in new[] { claim with { OrganizationId = Guid.NewGuid() }, claim with { ActorId = Guid.NewGuid() },
            claim with { LeaseId = Guid.NewGuid() }, claim with { WorkerId = Guid.NewGuid() } })
            Require(!await store.MarkReadyAsync(bad, eventId, ct), "Lifecycle tenant/actor/lease/worker fence failed.");
        Require(!await store.MarkReadyAsync(claim, Guid.NewGuid(), ct), "Lifecycle event identity fence failed.");
        var apiDenied = false;
        try { await new PostgresOrganizationLifecycleDeliveryStore(api).MarkReadyAsync(claim, eventId, ct); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { apiDenied = true; }
        Require(apiDenied && await ReadyAt() is null, "API gained lifecycle delivery authority.");
        async Task Admin(string sql)
        { await using var command = new NpgsqlCommand(sql, admin); await command.ExecuteNonQueryAsync(ct); }
        await Admin($"""
            CREATE FUNCTION public.ci_lifecycle_delivery_expiry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.tenant_id='{tenant:D}'::uuid THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;
             END IF; RETURN NEW; END $$;
            CREATE TRIGGER ci_lifecycle_delivery_expiry AFTER UPDATE OF ready_at ON organization_lifecycle_events
             FOR EACH ROW EXECUTE FUNCTION public.ci_lifecycle_delivery_expiry();
            """);
        try
        {
            var expired = false;
            try { await store.MarkReadyAsync(claim, eventId, ct); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation) { expired = true; }
            Require(expired && await ReadyAt() is null, "Late lifecycle delivery expiry retained readiness.");
        }
        finally { await Admin("DROP TRIGGER ci_lifecycle_delivery_expiry ON organization_lifecycle_events; DROP FUNCTION public.ci_lifecycle_delivery_expiry();"); }
        await new OrganizationLifecycleDeliveryHandler(store).ExecuteAsync(claim, ct);
        var originalReady = await ReadyAt(); Require(originalReady is not null, "Valid lifecycle delivery did not become ready.");
        await new OrganizationLifecycleDeliveryHandler(store).ExecuteAsync(claim, ct);
        Require(await ReadyAt() == originalReady, "Duplicate delivery rewrote readiness.");
        await Admin($"UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;");
        Require(!await store.MarkReadyAsync(claim, eventId, ct), "Expired duplicate delivery succeeded.");
        var reclaimed = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Lifecycle delivery was not reclaimed.");
        Require(reclaimed.Id == claim.Id && reclaimed.LeaseId != claim.LeaseId, "Lifecycle reclaim lost stable job identity.");
        Require(!await store.MarkReadyAsync(claim, eventId, ct), "Superseded delivery claim succeeded.");
        await new OrganizationLifecycleDeliveryHandler(store).ExecuteAsync(reclaimed, ct);
        Require(await ReadyAt() == originalReady && await jobs.CompleteAsync(tenant, reclaimed.Id, reclaimed.LeaseId, reclaimed.WorkerId, ct),
            "Lifecycle reclaimed delivery lost original readiness or acknowledgment.");
        foreach (var factory in new[] { api, worker })
        {
            await using var session = await factory.OpenTenantSessionAsync(tenant, ct);
            await using var query = new NpgsqlCommand("UPDATE organization_lifecycle_events SET ready_at=clock_timestamp() WHERE tenant_id=@tenant", session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); var denied = false;
            try { await query.ExecuteNonQueryAsync(ct); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { denied = true; }
            Require(denied, "Runtime acquired direct lifecycle event mutation.");
        }
        Require(actor == claim.ActorId, "Lifecycle delivery changed retained actor.");
        await OrganizationLifecycleReplayContract.RunAsync(admin, apiConnection, tenant, actor, eventId, true, ct);
        Console.WriteLine("Organization lifecycle delivery: restricted claim fences, late readiness rollback, duplicate/reclaimed delivery and disabled direct mutation passed.");
    }
}
