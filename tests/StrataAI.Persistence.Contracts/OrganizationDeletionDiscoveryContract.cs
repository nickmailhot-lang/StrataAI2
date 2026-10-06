using Npgsql;
using NpgsqlTypes;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

// PRD-03 / ARCH-07: bounded routing across accepted roots does not grant
// global graph/queue reads or bypass the subsequent explicit tenant lease.
internal static class OrganizationDeletionDiscoveryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        static Guid Id(int value) => Guid.Parse($"d0930000-0000-4000-8000-{value:000000000000}");
        var actor = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash)
             VALUES(@actor,@email,upper(@email),'Discovery fixture','ACTIVE','unused-contract-hash');
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"deletion-discovery-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        for (var i = 1; i <= 105; i++)
        {
            await using var seed = new NpgsqlCommand("""
                INSERT INTO organizations(id,name,owner_user_id,status,version) VALUES(@tenant,'Discovery fixture',@actor,'DELETING',2);
                INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
                INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id)
                 VALUES(@tenant,@request,@actor,2,'discovery-fixture');
                INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase) VALUES(@tenant,@request,@request,'ATTACHMENTS');
                INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,available_at)
                 VALUES(gen_random_uuid(),@tenant,'ORGANIZATION_DELETE_PAGE','discovery-fixture',@actor,'organization-lifecycle',
                 'discovery-fixture',jsonb_build_object('requestId',@request,'stepId',@request,'acceptedVersion',2),clock_timestamp());
                """, admin);
            seed.Parameters.AddWithValue("tenant", Id(i)); seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("request", Guid.NewGuid());
            await seed.ExecuteNonQueryAsync(ct);
        }
        await using (var exclude = new NpgsqlCommand("""
            UPDATE background_jobs SET available_at=clock_timestamp()+interval '1 hour' WHERE tenant_id=@future;
            UPDATE background_jobs SET service_identity='untrusted' WHERE tenant_id=@service;
            UPDATE background_jobs SET state='RUNNING',attempt_count=1,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()+interval '1 hour' WHERE tenant_id=@live;
            UPDATE background_jobs SET state='RUNNING',attempt_count=1,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()-interval '1 second' WHERE tenant_id=@crash;
            """, admin))
        {
            exclude.Parameters.AddWithValue("future", Id(102)); exclude.Parameters.AddWithValue("service", Id(103));
            exclude.Parameters.AddWithValue("live", Id(104)); exclude.Parameters.AddWithValue("crash", Id(105));
            await exclude.ExecuteNonQueryAsync(ct);
        }
        async Task<string> Snapshot()
        {
            await using var query = new NpgsqlCommand("""
                SELECT md5(jsonb_build_object(
                 'roots',(SELECT jsonb_agg(to_jsonb(r) ORDER BY tenant_id) FROM organization_deletion_requests r WHERE actor_id=@actor),
                 'progress',(SELECT jsonb_agg(to_jsonb(p) ORDER BY tenant_id) FROM organization_deletion_progress p
                  JOIN organization_deletion_requests r USING(tenant_id,request_id) WHERE r.actor_id=@actor),
                 'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE actor_id=@actor))::text);
                """, admin);
            query.Parameters.AddWithValue("actor", actor); return (string)(await query.ExecuteScalarAsync(ct))!;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var before = await Snapshot();
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var reader = new PostgresOrganizationDeletionScopeReader(worker);
        var first = await reader.ReadAsync(Id(0), 100, ct);
        Require(first.SequenceEqual(Enumerable.Range(1, 100).Select(Id)), "Discovery did not return its bounded ordered first page.");
        var second = await reader.ReadAsync(first[^1], 100, ct);
        Require(second.Contains(Id(101)) && second.Contains(Id(105)) && second.Count <= 100,
            "Discovery missed later Organizations or an expired crash lease.");
        Require(!second.Any(id => id == Id(102) || id == Id(103) || id == Id(104)) && !second.Intersect(first).Any(),
            "Discovery included delayed, mismatched, live-leased or duplicate work.");
        Require((await reader.ReadAsync(Id(0), 1, ct)).SequenceEqual(new[] { Id(1) }), "Discovery wrap did not revisit eligible lower UUIDs.");
        await using (var connection = await worker.OpenConnectionAsync(ct))
        {
            await using var raw = new NpgsqlCommand("SELECT count(*) FROM organization_deletion_requests;", connection);
            Require((long)(await raw.ExecuteScalarAsync(ct))! == 0, "Worker routing widened direct forced-RLS reads.");
            foreach (var limit in new[] { 0, 101 })
            {
                await using var invalid = new NpgsqlCommand("SELECT * FROM discover_organization_deletion_scopes(NULL,@limit);", connection);
                invalid.Parameters.AddWithValue("limit", limit);
                try { await invalid.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Unbounded routing limit was accepted."); }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InvalidParameterValue) { }
            }
        }
        await using var api = new PostgresConnectionFactory(apiConnection);
        try
        {
            await new PostgresOrganizationDeletionScopeReader(api).ReadAsync(null, 100, ct);
            throw new InvalidOperationException("API obtained global deletion routing references.");
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        Require(before == await Snapshot(), "Discovery mutated root, checkpoint or queue state.");
        Console.WriteLine("Organization deletion discovery: bounded seek/wrap beyond 100 scopes, delay/service/live-lease exclusion, crash eligibility, API denial, forced-RLS isolation and unchanged routing state passed.");
    }
}
