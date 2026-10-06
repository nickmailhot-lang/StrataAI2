using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

internal static class OrganizationMetadataDeliveryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, Guid tenant, Guid actor, CancellationToken ct)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var jobs = new PostgresBackgroundJobStore(worker);
        var claim = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Metadata event claim missing.");
        Require(claim.JobType == OrganizationMetadataDeliveryHandler.Type, "Metadata fixture claimed the wrong job.");
        var eventId = OrganizationLifecycleDeliveryHandler.ParseEventId(claim.SafeMetadataJson);
        var store = new PostgresOrganizationMetadataDeliveryStore(worker);
        async Task<DateTime?> ReadyAt()
        {
            await using var query = new NpgsqlCommand("SELECT ready_at FROM organization_metadata_events WHERE tenant_id=@tenant AND event_id=@event", admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("event", eventId);
            var result = await query.ExecuteScalarAsync(ct); return result is DateTime value ? value : null;
        }
        Require(await ReadyAt() is null, "Metadata event was ready before Worker delivery.");
        foreach (var bad in new[] { claim with { OrganizationId = Guid.NewGuid() }, claim with { ActorId = Guid.NewGuid() },
            claim with { LeaseId = Guid.NewGuid() }, claim with { WorkerId = Guid.NewGuid() } })
            Require(!await store.MarkReadyAsync(bad, eventId, ct), "Metadata tenant/actor/lease/worker fence failed.");
        Require(!await store.MarkReadyAsync(claim, Guid.NewGuid(), ct), "Metadata event identity fence failed.");
        var apiDenied = false;
        try { await new PostgresOrganizationMetadataDeliveryStore(api).MarkReadyAsync(claim, eventId, ct); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { apiDenied = true; }
        Require(apiDenied && await ReadyAt() is null, "API gained metadata delivery authority.");
        async Task Admin(string sql)
        { await using var command = new NpgsqlCommand(sql, admin); await command.ExecuteNonQueryAsync(ct); }
        await Admin($"""
            CREATE FUNCTION public.ci_metadata_delivery_expiry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.tenant_id='{tenant:D}'::uuid THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;
             END IF; RETURN NEW; END $$;
            CREATE TRIGGER ci_metadata_delivery_expiry AFTER UPDATE OF ready_at ON organization_metadata_events
             FOR EACH ROW EXECUTE FUNCTION public.ci_metadata_delivery_expiry();
            """);
        try
        {
            var expired = false;
            try { await store.MarkReadyAsync(claim, eventId, ct); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation) { expired = true; }
            Require(expired && await ReadyAt() is null, "Late metadata delivery expiry retained readiness.");
        }
        finally { await Admin("DROP TRIGGER ci_metadata_delivery_expiry ON organization_metadata_events; DROP FUNCTION public.ci_metadata_delivery_expiry();"); }
        await new OrganizationMetadataDeliveryHandler(store).ExecuteAsync(claim, ct);
        var originalReady = await ReadyAt(); Require(originalReady is not null, "Valid metadata delivery did not become ready.");
        await new OrganizationMetadataDeliveryHandler(store).ExecuteAsync(claim, ct);
        Require(await ReadyAt() == originalReady, "Duplicate delivery rewrote readiness.");
        await Admin($"UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;");
        Require(!await store.MarkReadyAsync(claim, eventId, ct), "Expired duplicate delivery succeeded.");
        var reclaimed = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Metadata delivery was not reclaimed.");
        Require(reclaimed.Id == claim.Id && reclaimed.LeaseId != claim.LeaseId, "Metadata reclaim lost stable job identity.");
        Require(!await store.MarkReadyAsync(claim, eventId, ct), "Superseded delivery claim succeeded.");
        await new OrganizationMetadataDeliveryHandler(store).ExecuteAsync(reclaimed, ct);
        Require(await ReadyAt() == originalReady && await jobs.CompleteAsync(tenant, reclaimed.Id, reclaimed.LeaseId, reclaimed.WorkerId, ct),
            "Metadata reclaimed delivery lost original readiness or acknowledgment.");
        foreach (var factory in new[] { api, worker })
        {
            await using var session = await factory.OpenTenantSessionAsync(tenant, ct);
            await using var query = new NpgsqlCommand("UPDATE organization_metadata_events SET ready_at=clock_timestamp() WHERE tenant_id=@tenant", session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("event", eventId); var denied = false;
            try { await query.ExecuteNonQueryAsync(ct); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { denied = true; }
            Require(denied, "Runtime acquired direct metadata event mutation.");
        }
        Require(actor == claim.ActorId, "Metadata delivery changed retained actor.");
        // A committed event is historical evidence, not a new action by its
        // actor. Departure cannot strand the remaining committed update.
        await Admin($"UPDATE organization_members SET status='REMOVED',version=version+1 WHERE tenant_id='{tenant:D}'::uuid AND user_id='{actor:D}'::uuid;");
        var next = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Committed update delivery was missing.");
        var nextEvent = OrganizationLifecycleDeliveryHandler.ParseEventId(next.SafeMetadataJson);
        Require(next.JobType == OrganizationMetadataDeliveryHandler.Type && nextEvent != eventId,
            "Committed update delivery reused the prior event.");
        await new OrganizationMetadataDeliveryHandler(store).ExecuteAsync(next, ct);
        Require(await jobs.CompleteAsync(tenant, next.Id, next.LeaseId, next.WorkerId, ct),
            "Committed update delivery was stranded by actor departure.");
        Console.WriteLine("Organization metadata delivery: restricted claim fences, late readiness rollback, duplicate/reclaimed delivery and disabled direct mutation passed.");
    }
}
