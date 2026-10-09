using Npgsql;
using StrataAI.Infrastructure.Persistence;

// PRD-03/ARCH-04/07: storage admission and restricted-role boundaries only.
// This is not HTTP authorization, graph processing or terminal completion.
internal static class OrganizationDeletionProgressContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var request = Guid.NewGuid(); var foreign = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Deletion progress fixture','ACTIVE','unused-contract-hash',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Deletion progress fixture',@actor,'DELETING',2,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(@member,@tenant,@actor,'OWNER','ACTIVE');
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("tenant", tenant);
            seed.Parameters.AddWithValue("member", Guid.NewGuid()); seed.Parameters.AddWithValue("email", $"deletion-progress-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        const string insert = """
            INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id)
             VALUES(@tenant,@request,@actor,2,'deletion-progress-test');
            INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase)
             VALUES(@tenant,@request,@request,'ATTACHMENTS');
            """;
        NpgsqlCommand Query(TenantDbSession session, string sql)
        {
            var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("request", request); command.Parameters.AddWithValue("actor", actor);
            return command;
        }
        await using (var rollback = await api.OpenTenantSessionAsync(tenant, ct))
        { await using var command = Query(rollback, insert); await command.ExecuteNonQueryAsync(ct); }
        await using (var inspect = new NpgsqlCommand("SELECT count(*) FROM organization_deletion_requests WHERE tenant_id=@tenant", admin))
        {
            inspect.Parameters.AddWithValue("tenant", tenant);
            if ((long)(await inspect.ExecuteScalarAsync(ct))! != 0) throw new InvalidOperationException("Deletion request escaped rollback.");
        }
        await using (var commit = await api.OpenTenantSessionAsync(tenant, ct))
        { await using var command = Query(commit, insert); await command.ExecuteNonQueryAsync(ct); await commit.CommitAsync(ct); }
        await using (var clocks = new NpgsqlCommand("""
            SELECT p.created_at=r.created_at AND isfinite(p.created_at) AND isfinite(p.updated_at) AND p.updated_at>=p.created_at
            FROM organization_deletion_progress p JOIN organization_deletion_requests r USING(tenant_id,request_id) WHERE p.tenant_id=@tenant;
            """, admin))
        {
            clocks.Parameters.AddWithValue("tenant", tenant);
            if (await clocks.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Deletion progress lost its exact accepted-request creation clock.");
        }
        foreach (var factory in new[] { api, worker })
        {
            await using (var own = await factory.OpenTenantSessionAsync(tenant, ct))
            {
                await using var command = Query(own, "SELECT count(*) FROM organization_deletion_progress WHERE tenant_id=@tenant AND request_id=@request AND phase='ATTACHMENTS' AND version=1 AND after_id IS NULL AND completed_at IS NULL");
                if ((long)(await command.ExecuteScalarAsync(ct))! != 1) throw new InvalidOperationException("Initial deletion progress was not canonical.");
            }
            await using (var other = await factory.OpenTenantSessionAsync(foreign, ct))
            {
                await using var command = Query(other, "SELECT count(*) FROM organization_deletion_requests WHERE tenant_id=@tenant");
                if ((long)(await command.ExecuteScalarAsync(ct))! != 0) throw new InvalidOperationException("Deletion request crossed tenant scope.");
            }
        }
        foreach (var (factory, sql) in new[] {
            (api, "UPDATE organization_deletion_requests SET accepted_version=3 WHERE tenant_id=@tenant"),
            (api, "DELETE FROM organization_deletion_progress WHERE tenant_id=@tenant"),
            (worker, "UPDATE organization_deletion_progress SET phase='COMPLETE',completed_at=now() WHERE tenant_id=@tenant"),
            (worker, "SELECT correlation_id FROM organization_deletion_requests WHERE tenant_id=@tenant") })
        {
            await using var session = await factory.OpenTenantSessionAsync(tenant, ct); await using var command = Query(session, sql);
            var denied = false;
            try { await command.ExecuteNonQueryAsync(ct); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { denied = true; }
            if (!denied) throw new InvalidOperationException("Deletion progress capability exceeded its initial boundary.");
        }
        foreach (var sql in new[] {
            "INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id) VALUES(@tenant,@request,@actor,3,'test')",
            "INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase) VALUES(@tenant,@request,@request,'FINALIZE')" })
        {
            await using var session = await api.OpenTenantSessionAsync(tenant, ct); await using var command = Query(session, sql);
            var refused = false;
            try { await command.ExecuteNonQueryAsync(ct); }
            catch (PostgresException error) when (error.SqlState == "P0001") { refused = true; }
            if (!refused) throw new InvalidOperationException("Deletion progress bypassed its parent or initial-phase gate.");
        }
        // Retain immutable fixture/request references until this isolated CI database is discarded.
        Console.WriteLine("Restricted deletion progress: atomic rollback, canonical initial checkpoint, cross-tenant refusal and disabled Worker mutation passed.");
    }
}
