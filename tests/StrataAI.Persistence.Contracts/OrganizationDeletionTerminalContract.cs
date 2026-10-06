using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.Organizations;

// Real restricted Worker terminal capability. Earlier page processing is staged
// by the admin fixture; this does not prove graph traversal or browser acceptance.
internal static class OrganizationDeletionTerminalContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var request = Guid.NewGuid();
        var board = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid(); var attachment = Guid.NewGuid();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Admin(string sql)
        {
            await using var command = new NpgsqlCommand(sql, admin);
            command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("actor", actor);
            command.Parameters.AddWithValue("member", Guid.NewGuid()); command.Parameters.AddWithValue("request", request);
            command.Parameters.AddWithValue("board", board); command.Parameters.AddWithValue("list", list);
            command.Parameters.AddWithValue("card", card); command.Parameters.AddWithValue("attachment", attachment);
            command.Parameters.AddWithValue("email", $"terminal-{actor:N}@example.test");
            await command.ExecuteNonQueryAsync(ct);
        }
        await Admin("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Terminal fixture','ACTIVE','unused',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Terminal fixture',@actor,'DELETING',2,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(@member,@tenant,@actor,'OWNER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Retained Board',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
             VALUES(@list,@tenant,@board,'Retained List','500000000000000000000000000000',now(),now());
            UPDATE board_lists SET lifecycle_state='ARCHIVED',archived_at=now(),updated_at=now(),version=2 WHERE id=@list;
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
             VALUES(@card,@tenant,@board,@list,'Retained Card','500000000000000000000000000000',now(),now());
            INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
             VALUES(@attachment,@tenant,@card,@actor,'URL','Retained link','https://example.test/retained','NOT_APPLICABLE',now(),now());
            """);
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var job = OrganizationDeletionJobs.Create(tenant, actor, new(request, request, 2), "terminal-original");
        await using (var publish = await api.OpenTenantSessionAsync(tenant, ct))
        {
            await using var root = new NpgsqlCommand("""
                INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id)
                 VALUES(@tenant,@request,@actor,2,'terminal-original');
                INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase)
                 VALUES(@tenant,@request,@request,'ATTACHMENTS');
                """, publish.Connection, publish.Transaction);
            root.Parameters.AddWithValue("tenant", tenant); root.Parameters.AddWithValue("request", request); root.Parameters.AddWithValue("actor", actor);
            await root.ExecuteNonQueryAsync(ct);
            Require(await new PostgresBackgroundJobStore(api).PublishAsync(publish, job, ct), "Terminal fixture publication failed.");
            await publish.CommitAsync(ct);
        }
        var claim = await new PostgresBackgroundJobStore(worker).ClaimAsync(tenant, Guid.NewGuid(), ct)
            ?? throw new InvalidOperationException("Terminal fixture claim failed.");
        Task<bool> Finish(PostgresConnectionFactory factory, Guid scope, Guid lease, Guid key) =>
            new PostgresOrganizationDeletionFinalizer(factory).FinishAsync(
                claim with { OrganizationId = scope, LeaseId = lease }, new(key, request, 2), ct);
        async Task Snapshot(string status, long count)
        {
            await using var command = new NpgsqlCommand("""
                SELECT o.status,o.version,p.phase,
                 (SELECT count(*) FROM organization_lifecycle_events WHERE tenant_id=@tenant),
                 (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant AND event_type='ORGANIZATION_DELETED'),
                 (SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ORGANIZATION_LIFECYCLE_EVENT_READY')
                FROM organizations o JOIN organization_deletion_progress p ON p.tenant_id=o.id WHERE o.id=@tenant;
                """, admin);
            command.Parameters.AddWithValue("tenant", tenant);
            await using var rows = await command.ExecuteReaderAsync(ct);
            Require(await rows.ReadAsync(ct) && rows.GetString(0) == status && rows.GetInt64(1) == 2 + count
                && rows.GetString(2) == (count == 0 ? "FINALIZE" : "COMPLETE")
                && rows.GetInt64(3) == count && rows.GetInt64(4) == count && rows.GetInt64(5) == count,
                "Terminal state, checkpoint, audit, event and delivery were not atomic.");
        }
        Require(!await Finish(worker, tenant, claim.LeaseId, request), "Non-final stage completed.");
        await Admin("UPDATE organization_deletion_progress SET phase='FINALIZE',version=2 WHERE tenant_id=@tenant;");
        foreach (var (scope, lease, key) in new[] { (Guid.NewGuid(), claim.LeaseId, request), (tenant, Guid.NewGuid(), request), (tenant, claim.LeaseId, Guid.NewGuid()) })
            Require(!await Finish(worker, scope, lease, key), "Terminal scope/lease/request fence failed.");
        var apiDenied = false;
        try { await Finish(api, tenant, claim.LeaseId, request); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { apiDenied = true; }
        Require(apiDenied, "API acquired Worker terminal capability.");
        foreach (var mutation in new[] {
            "UPDATE boards SET lifecycle_state='DELETED',deleted_by=@actor,deleted_at=now(),updated_at=now(),version=version+1 WHERE id=@board",
            "UPDATE board_lists SET lifecycle_state='DELETED',deleted_by=@actor,deleted_at=now(),updated_at=now(),version=version+1 WHERE id=@list",
            "UPDATE cards SET lifecycle_state='DELETED',deleted_by=@actor,deleted_at=now(),updated_at=now(),version=version+1 WHERE id=@card" })
        {
            Require(!await Finish(worker, tenant, claim.LeaseId, request), "Terminal gate ignored a remaining descendant.");
            await Snapshot("DELETING", 0); await Admin(mutation);
        }
        Require(!await Finish(worker, tenant, claim.LeaseId, request), "Terminal gate ignored an attachment.");
        await Admin("""
            UPDATE attachments SET lifecycle_state='ARCHIVED',archived_at=now(),updated_at=now(),version=version+1 WHERE id=@attachment;
            UPDATE attachments SET lifecycle_state='DELETED',deleted_by=@actor,deleted_at=now(),updated_at=now(),version=version+1 WHERE id=@attachment;
            """);
        // Inject expiry after the last queue write, not merely before admission.
        await Admin($"""
            CREATE FUNCTION public.ci_terminal_expiry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.tenant_id='{tenant:D}'::uuid AND NEW.job_type='ORGANIZATION_LIFECYCLE_EVENT_READY' THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;
             END IF; RETURN NEW; END $$;
            CREATE TRIGGER ci_terminal_expiry AFTER INSERT ON background_jobs FOR EACH ROW EXECUTE FUNCTION public.ci_terminal_expiry();
            """);
        try
        {
            var expired = false;
            try { await Finish(worker, tenant, claim.LeaseId, request); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation) { expired = true; }
            Require(expired, "Late terminal lease expiry was accepted."); await Snapshot("DELETING", 0);
        }
        finally { await Admin("DROP TRIGGER ci_terminal_expiry ON background_jobs; DROP FUNCTION public.ci_terminal_expiry();"); }
        Require(await Finish(worker, tenant, claim.LeaseId, request), "Empty authoritative graph did not complete.");
        await Snapshot("DELETED", 1);
        Require(await Finish(worker, tenant, claim.LeaseId, request), "Committed terminal replay failed.");
        await Snapshot("DELETED", 1);
        await using (var retained = new NpgsqlCommand("""
            SELECT o.deleted_by,o.deleted_at,o.updated_at,p.completed_at,e.actor_id,e.entity_version,e.correlation_id,e.metadata,
             j.safe_metadata,j.actor_id,j.service_identity,
             (SELECT count(*) FROM boards WHERE tenant_id=@tenant),
             (SELECT count(*) FROM board_lists WHERE tenant_id=@tenant),
             (SELECT count(*) FROM cards WHERE tenant_id=@tenant),
             (SELECT count(*) FROM attachments WHERE tenant_id=@tenant)
            FROM organizations o JOIN organization_deletion_progress p ON p.tenant_id=o.id
             JOIN organization_lifecycle_events e ON e.tenant_id=o.id
             JOIN background_jobs j ON j.tenant_id=o.id AND j.job_type='ORGANIZATION_LIFECYCLE_EVENT_READY'
            WHERE o.id=@tenant AND j.safe_metadata=jsonb_build_object('eventId',e.event_id);
            """, admin))
        {
            retained.Parameters.AddWithValue("tenant", tenant);
            await using var rows = await retained.ExecuteReaderAsync(ct);
            Require(await rows.ReadAsync(ct) && rows.GetGuid(0) == actor && rows.GetDateTime(1) == rows.GetDateTime(2)
                && rows.GetDateTime(1) == rows.GetDateTime(3) && rows.GetGuid(4) == actor && rows.GetInt64(5) == 3
                && rows.GetString(6) == "terminal-original" && rows.GetString(7) == "{}" && rows.GetGuid(9) == actor
                && rows.GetString(10) == "organization-lifecycle-delivery"
                && Enumerable.Range(11, 4).All(i => rows.GetInt64(i) == 1),
                "Terminal attribution, delivery references or retained descendant identities were lost.");
        }
        foreach (var mutation in new[] { "UPDATE organizations SET status='ACTIVE',deleted_by=NULL,deleted_at=NULL WHERE id=@tenant", "DELETE FROM organizations WHERE id=@tenant", "UPDATE organization_lifecycle_events SET actor_id=gen_random_uuid() WHERE tenant_id=@tenant" })
        {
            var immutable = false;
            try { await Admin(mutation); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation) { immutable = true; }
            Require(immutable, "Terminal state or attribution was mutable.");
        }
        await using (var foreign = await api.OpenTenantSessionAsync(Guid.NewGuid(), ct))
        {
            await using var query = new NpgsqlCommand("SELECT count(*) FROM organization_lifecycle_events WHERE tenant_id=@tenant", foreign.Connection, foreign.Transaction);
            query.Parameters.AddWithValue("tenant", tenant);
            Require((long)(await query.ExecuteScalarAsync(ct))! == 0, "Terminal event crossed tenant scope.");
        }
        await OrganizationLifecycleDeliveryContract.RunAsync(admin,apiConnection,workerConnection,tenant,actor,ct);
        Console.WriteLine("Organization terminal gate: descendant proof, restricted authority, late lease rollback, immutable completion/event and duplicate recovery passed.");
    }
}
