using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

// PRD-03 / ARCH-07: automatic routing returns bounded references while typed
// invoker claims retain forced RLS and leave unrelated provider queues alone.
internal static class OrganizationMetadataDiscoveryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        static Guid Id(int value) => Guid.Parse($"d0960000-0000-4000-8000-{value:000000000000}");
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var actor = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Metadata discovery','ACTIVE','unused-contract-hash',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"metadata-discovery-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        for (var i = 1; i <= 109; i++)
        {
            await using var seed = new NpgsqlCommand("""
                INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
                 VALUES(@tenant,'Metadata routing',@actor,'ACTIVE',1,now(),now());
                INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
                INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
                 VALUES(gen_random_uuid(),@tenant,@actor,'ORGANIZATION_CREATED','Organization',@tenant,'metadata-routing','{}');
                """, admin);
            seed.Parameters.AddWithValue("tenant", Id(i)); seed.Parameters.AddWithValue("actor", actor);
            await seed.ExecuteNonQueryAsync(ct);
        }
        async Task Admin(string sql)
        { await using var command = new NpgsqlCommand(sql, admin); await command.ExecuteNonQueryAsync(ct); }
        await Admin($"""
            UPDATE background_jobs SET available_at=clock_timestamp()+interval '1 hour' WHERE tenant_id='{Id(102):D}';
            UPDATE background_jobs SET service_identity='untrusted' WHERE tenant_id='{Id(103):D}';
            UPDATE background_jobs SET state='RUNNING',attempt_count=1,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()+interval '1 hour' WHERE tenant_id='{Id(104):D}';
            UPDATE background_jobs SET state='RUNNING',attempt_count=1,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()-interval '1 second' WHERE tenant_id='{Id(105):D}';
            UPDATE background_jobs SET actor_id=gen_random_uuid() WHERE tenant_id='{Id(106):D}';
            UPDATE background_jobs SET correlation_id='mismatch' WHERE tenant_id='{Id(107):D}';
            UPDATE background_jobs SET safe_metadata=jsonb_build_object('eventId',gen_random_uuid()) WHERE tenant_id='{Id(108):D}';
            UPDATE background_jobs SET state='RUNNING',attempt_count=max_attempts,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()-interval '1 second' WHERE tenant_id='{Id(109):D}';
            INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,
             state,attempt_count,max_attempts,lease_id,worker_id,lease_expires_at,available_at)
             VALUES(gen_random_uuid(),'{Id(1):D}','UNRELATED_PROVIDER','metadata-isolation','{actor:D}','other-provider','metadata-routing',
             'RUNNING',1,1,gen_random_uuid(),gen_random_uuid(),clock_timestamp()-interval '1 second',clock_timestamp()-interval '1 day');
            """);
        async Task<string> Snapshot()
        {
            await using var query = new NpgsqlCommand("""
                SELECT md5(jsonb_build_object(
                 'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY tenant_id,sequence) FROM organization_metadata_events e WHERE actor_id=@actor),
                 'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j
                    WHERE tenant_id IN (SELECT tenant_id FROM organization_metadata_events WHERE actor_id=@actor)))::text);
                """, admin);
            query.Parameters.AddWithValue("actor", actor); return (string)(await query.ExecuteScalarAsync(ct))!;
        }
        var before = await Snapshot();
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var scopes = new PostgresOrganizationMetadataScopeReader(worker);
        var first = await scopes.ReadAsync(Id(0), 100, ct);
        Require(first.SequenceEqual(Enumerable.Range(1, 100).Select(Id)), "Metadata routing first page was not bounded/ordered.");
        var second = await scopes.ReadAsync(first[^1], 100, ct);
        Require(new[] { Id(101), Id(105), Id(109) }.All(second.Contains) && second.Count <= 100 && !second.Intersect(first).Any(),
            "Metadata routing missed later, crash or terminal-expiry scopes.");
        Require(!new[] { Id(102), Id(103), Id(104), Id(106), Id(107), Id(108) }.Any(second.Contains),
            "Metadata routing admitted delayed, live-leased or mismatched source jobs.");
        Require((await scopes.ReadAsync(Id(0), 1, ct)).SequenceEqual(new[] { Id(1) }), "Metadata routing wrap missed lower UUIDs.");
        await using (var connection = await worker.OpenConnectionAsync(ct))
        {
            await using var queue = new NpgsqlCommand("SELECT count(*) FROM background_jobs", connection);
            Require((long)(await queue.ExecuteScalarAsync(ct))! == 0, "Metadata discovery widened direct queue RLS.");
            foreach (var limit in new[] { "NULL", "0", "101" })
            {
                await using var invalid = new NpgsqlCommand($"SELECT * FROM discover_organization_metadata_scopes(NULL,{limit})", connection);
                try { await invalid.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Unbounded metadata routing was accepted."); }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InvalidParameterValue) { }
            }
            await using var content = new NpgsqlCommand("SELECT count(*) FROM organization_metadata_events", connection);
            try { await content.ExecuteScalarAsync(ct); throw new InvalidOperationException("Worker gained direct metadata source content."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using var api = new PostgresConnectionFactory(apiConnection);
        try { await new PostgresOrganizationMetadataScopeReader(api).ReadAsync(null, 100, ct); throw new InvalidOperationException("API gained global metadata routing."); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        Require(before == await Snapshot(), "Metadata routing mutated source or queue state.");
        var jobs = new PostgresBackgroundJobStore(worker, metadataJobsOnly: true);
        var claim = await jobs.ClaimAsync(Id(1), Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Typed metadata claim was missing.");
        Require(claim.JobType == OrganizationMetadataDeliveryHandler.Type && claim.OrganizationId == Id(1), "Typed metadata claim consumed a provider or foreign tenant.");
        await new OrganizationMetadataDeliveryHandler(new PostgresOrganizationMetadataDeliveryStore(worker)).ExecuteAsync(claim, ct);
        Require(await jobs.CompleteAsync(claim.OrganizationId, claim.Id, claim.LeaseId, claim.WorkerId, ct)
            && await jobs.ClaimAsync(Id(1), Guid.NewGuid(), ct) is null, "Typed metadata completion consumed unrelated provider work.");
        await using (var provider = new NpgsqlCommand($"SELECT count(*) FROM background_jobs WHERE tenant_id='{Id(1):D}' AND job_type='UNRELATED_PROVIDER' AND state='RUNNING' AND attempt_count=1", admin))
            Require((long)(await provider.ExecuteScalarAsync(ct))! == 1, "Typed metadata claim retired unrelated provider work.");
        Require(await jobs.ClaimAsync(Id(109), Guid.NewGuid(), ct) is null, "Exhausted metadata lease was reclaimed beyond its limit.");
        await using (var expired = new NpgsqlCommand($"SELECT count(*) FROM background_jobs WHERE tenant_id='{Id(109):D}' AND state='FAILED' AND last_error_code='lease_expired'", admin))
            Require((long)(await expired.ExecuteScalarAsync(ct))! == 1, "Final crashed metadata attempt was not retired.");
        Console.WriteLine("Organization metadata discovery: bounded seek/wrap beyond 100 scopes, source/job binding, delay/live-lease exclusion, crash recovery, API/content denial, routing immutability, typed tenant claims and provider isolation passed.");
    }
}
