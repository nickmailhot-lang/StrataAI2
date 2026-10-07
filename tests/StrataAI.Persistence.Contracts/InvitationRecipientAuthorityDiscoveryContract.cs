using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Persistence;

internal static class InvitationRecipientAuthorityDiscoveryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        static Guid Id(int value) => Guid.Parse($"d1060000-0000-4000-8000-{value:000000000000}");
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var actor = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Authority discovery','ACTIVE','unused-contract-hash',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"authority-discovery-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        for (var i = 1; i <= 110; i++)
        {
            await using var seed = new NpgsqlCommand("""
                INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
                 VALUES(@tenant,'Authority routing',@actor,'ACTIVE',1,now(),now());
                INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
                UPDATE organizations SET name='Changed authority',version=2,updated_at=clock_timestamp() WHERE id=@tenant;
                INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
                 VALUES(gen_random_uuid(),@tenant,@actor,'ORGANIZATION_UPDATED','Organization',@tenant,'authority-routing','{}');
                """, admin);
            seed.Parameters.AddWithValue("tenant", Id(i)); seed.Parameters.AddWithValue("actor", actor);
            await seed.ExecuteNonQueryAsync(ct);
        }
        async Task Admin(string sql)
        { await using var command = new NpgsqlCommand(sql, admin); await command.ExecuteNonQueryAsync(ct); }
        await Admin($"""
            UPDATE background_jobs SET available_at=clock_timestamp()+interval '1 hour'
             WHERE tenant_id='{Id(102)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET service_identity='untrusted'
             WHERE tenant_id='{Id(103)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET state='RUNNING',attempt_count=1,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()+interval '1 hour' WHERE tenant_id='{Id(104)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET state='RUNNING',attempt_count=1,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()-interval '1 second' WHERE tenant_id='{Id(105)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET actor_id=gen_random_uuid() WHERE tenant_id='{Id(106)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET correlation_id='mismatch' WHERE tenant_id='{Id(107)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET safe_metadata=jsonb_build_object('eventId',gen_random_uuid())
             WHERE tenant_id='{Id(108)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET state='RUNNING',attempt_count=max_attempts,lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
             lease_expires_at=clock_timestamp()-interval '1 second' WHERE tenant_id='{Id(109)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            UPDATE background_jobs SET idempotency_key='wrong-source-page' WHERE tenant_id='{Id(110)}' AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE';
            """);
        async Task<string> Snapshot()
        {
            await using var query = new NpgsqlCommand("""
                SELECT md5(jsonb_build_object(
                 'pages',(SELECT jsonb_agg(to_jsonb(p) ORDER BY tenant_id,job_id) FROM invitation_recipient_authority_pages p
                   WHERE tenant_id IN (SELECT id FROM organizations WHERE owner_user_id=@actor)),
                 'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j
                   WHERE tenant_id IN (SELECT id FROM organizations WHERE owner_user_id=@actor)))::text);
                """, admin);
            query.Parameters.AddWithValue("actor", actor); return (string)(await query.ExecuteScalarAsync(ct))!;
        }
        var before = await Snapshot();
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var scopes = new PostgresInvitationRecipientAuthorityScopeReader(worker);
        var first = await scopes.ReadAsync(Id(0), 100, ct);
        Require(first.SequenceEqual(Enumerable.Range(1, 100).Select(Id)), "Authority first scope page was not bounded/ordered.");
        var second = await scopes.ReadAsync(first[^1], 100, ct);
        Require(second.Count <= 100 && second.Where(id => id.ToString().StartsWith("d1060000-", StringComparison.Ordinal))
            .SequenceEqual(new[] { Id(101), Id(105), Id(109) }), "Authority discovery included delayed, live, malformed or unbound work.");
        Require((await scopes.ReadAsync(null, 100, ct)).Contains(Id(1)), "Authority wrap lost lower queued Organizations.");
        Require(await Snapshot() == before, "Authority routing mutated sources/pages/queue.");
        foreach (var limit in new[] { 0, 101 })
        {
            try { await scopes.ReadAsync(null, limit, ct); throw new InvalidOperationException("Unbounded authority scope admitted."); }
            catch (ArgumentOutOfRangeException) { }
        }
        await using var api = new PostgresConnectionFactory(apiConnection);
        try { await new PostgresInvitationRecipientAuthorityScopeReader(api).ReadAsync(null, 100, ct); throw new InvalidOperationException("API gained authority routing."); }
        catch (PostgresException error) when (error.SqlState == "42501") { }
        foreach (var factory in new[] { api, worker })
        {
            await using var connection = await factory.OpenConnectionAsync(ct);
            foreach (var table in new[] { "invitation_recipient_authority_pages", "invitation_recipient_authority_effects" })
            {
                await using var query = new NpgsqlCommand($"SELECT * FROM {table} LIMIT 1", connection);
                try { await query.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Runtime read private authority data."); }
                catch (PostgresException error) when (error.SqlState == "42501") { }
            }
        }
        var jobs = new PostgresBackgroundJobStore(worker, authorityJobsOnly: true);
        var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => jobs.ClaimAsync(Id(1), Guid.NewGuid(), ct)));
        var claim = claims.Single(value => value is not null)!;
        Require(claim.JobType == InvitationRecipientAuthorityDeliveryHandler.Type && claim.OrganizationId == Id(1), "Authority claim consumed metadata/foreign work.");
        var handler = new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker));
        await handler.ExecuteAsync(claim, ct);
        await handler.ExecuteAsync(claim, ct); // Committed page replay under the original still-live lease.
        Require(await jobs.CompleteAsync(Id(1), claim.Id, claim.LeaseId, claim.WorkerId, ct), "Authority page acknowledgment failed.");
        var reclaim = await jobs.ClaimAsync(Id(105), Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Expired authority lease was stranded.");
        await handler.ExecuteAsync(reclaim, ct);
        Require(await jobs.CompleteAsync(Id(105), reclaim.Id, reclaim.LeaseId, reclaim.WorkerId, ct), "Reclaimed authority acknowledgment failed.");
        Require(await jobs.ClaimAsync(Id(109), Guid.NewGuid(), ct) is null, "Exhausted authority lease was reclaimed.");
        await using var verify = new NpgsqlCommand("""
            SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ORGANIZATION_METADATA_EVENT_READY'
             AND state='PENDING' AND attempt_count=0;
            """, admin);
        verify.Parameters.AddWithValue("tenant", Id(1));
        Require((long)(await verify.ExecuteScalarAsync(ct))! == 1, "Authority claims changed an unrelated metadata queue.");
        Console.WriteLine("Recipient authority discovery: bounded 100-scope seek/wrap, canonical page/source binding, delay/live-lease exclusion, routing immutability, API/private-data denial, concurrent typed claims, committed replay and expired-lease recovery passed.");
    }
}
