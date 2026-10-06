using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

// Real restricted PostgreSQL and owning Organization unit. Actor admission is
// synthetic; this does not establish HTTP/session or terminal deletion acceptance.
internal static class OrganizationDeletionPublicationContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var request = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Deletion publication fixture','ACTIVE','unused-contract-hash',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Deletion publication fixture',@actor,'ACTIVE',1,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(@member,@tenant,@actor,'OWNER','ACTIVE');
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("tenant", tenant);
            seed.Parameters.AddWithValue("member", Guid.NewGuid()); seed.Parameters.AddWithValue("email", $"deletion-publication-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        var admission = new AdmissionFixture();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<ICommandActorAuthorization>(admission);
        services.AddStrataAiOrganizations(new(RuntimeMode.Production, "contract", "contract"));
        await using var provider = services.BuildServiceProvider();
        var connections = provider.GetRequiredService<PostgresConnectionFactory>();
        var jobs = new PostgresBackgroundJobStore(connections);
        var publisher = new PostgresOrganizationDeletionJobPublisher(connections, jobs);
        var unit = provider.GetRequiredService<IOrganizationUnitOfWork>();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task AssertSnapshot(string status, long version, long count, long? jobCount = null)
        {
            await using var query = new NpgsqlCommand("""
                SELECT status,version,
                 (SELECT count(*) FROM organization_deletion_requests WHERE tenant_id=@tenant),
                 (SELECT count(*) FROM organization_deletion_progress WHERE tenant_id=@tenant),
                 (SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ORGANIZATION_DELETE_PAGE')
                FROM organizations WHERE id=@tenant;
                """, admin);
            query.Parameters.AddWithValue("tenant", tenant);
            await using var rows = await query.ExecuteReaderAsync(ct);
            Require(await rows.ReadAsync(ct) && rows.GetString(0) == status && rows.GetInt64(1) == version
                && rows.GetInt64(2) == count && rows.GetInt64(3) == count && rows.GetInt64(4) == (jobCount ?? count),
                "Deletion parent/request/checkpoint/job lost their atomic boundary.");
        }
        async Task<OrganizationOperation<bool>> Accept(bool refuse)
        {
            return await unit.ExecuteAsync(tenant, actor, null, false, async () =>
            {
                await using var session = await connections.OpenTenantSessionAsync(tenant, ct);
                await using var change = new NpgsqlCommand("UPDATE organizations SET status='DELETING',version=2 WHERE id=@tenant AND status='ACTIVE' AND version=1", session.Connection, session.Transaction);
                change.Parameters.AddWithValue("tenant", tenant);
                Require(await change.ExecuteNonQueryAsync(ct) == 1, "Fixture parent transition failed.");
                Require(await publisher.PublishAsync(tenant, actor, request, 2, "original-publication", ct), "Initial publication was not created.");
                return refuse ? OrganizationOperation<bool>.Failure("fixture_refused") : OrganizationOperation<bool>.Success(true);
            }, ct);
        }
        var standaloneRefused = false;
        try { await publisher.PublishAsync(tenant, actor, request, 2, "standalone", ct); }
        catch (InvalidOperationException) { standaloneRefused = true; }
        Require(standaloneRefused, "Standalone publication escaped transaction ownership.");
        await AssertSnapshot("ACTIVE", 1, 0);
        var refused = await Accept(true);
        Require(!refused.Succeeded && refused.ErrorCode == "fixture_refused", "Owning command refusal was lost.");
        await AssertSnapshot("ACTIVE", 1, 0);
        admission.RefuseFinal = true; admission.Calls = 0;
        var expired = await Accept(false);
        Require(!expired.Succeeded && expired.ErrorCode == "session_unavailable", "Final actor refusal was lost.");
        await AssertSnapshot("ACTIVE", 1, 0);
        admission.RefuseFinal = false; admission.Calls = 0;
        // A colliding job with no accepted request must refuse, rolling back the
        // root/checkpoint/parent created before the final queue insert.
        await using (var collision = await connections.OpenTenantSessionAsync(tenant, ct))
        {
            Require(await jobs.PublishAsync(collision, OrganizationDeletionJobs.Create(tenant, actor,
                new(request, request, 2), "orphan-collision"), ct), "Collision fixture was not published.");
            await collision.CommitAsync(ct);
        }
        var collisionRefused = false;
        try { await Accept(false); }
        catch (InvalidOperationException) { collisionRefused = true; }
        Require(collisionRefused, "Partial existing publication was accepted.");
        await AssertSnapshot("ACTIVE", 1, 0, 1);
        await using (var remove = new NpgsqlCommand("DELETE FROM background_jobs WHERE tenant_id=@tenant AND job_type='ORGANIZATION_DELETE_PAGE'", admin))
        {
            remove.Parameters.AddWithValue("tenant", tenant); await remove.ExecuteNonQueryAsync(ct);
        }
        Require((await Accept(false)).Succeeded, "Atomic deletion publication failed.");
        await AssertSnapshot("DELETING", 2, 1);
        async Task<OrganizationOperation<bool>> Replay(Guid key, long acceptedVersion = 2) =>
            await unit.ExecuteAsync(tenant, actor, null, false, async () =>
                OrganizationOperation<bool>.Success(await publisher.PublishAsync(tenant, actor, key, acceptedVersion, "retry-correlation", ct)),
                ct, allowDeletionRecovery: true);
        var duplicates = await Task.WhenAll(Replay(request), Replay(request));
        Require(duplicates.All(x => x.Succeeded && !x.Value), "Concurrent replay republished a deletion job.");
        foreach (var mismatch in new[] { (Guid.NewGuid(), 2L), (request, 3L) })
        {
            var denied = false;
            try { await Replay(mismatch.Item1, mismatch.Item2); }
            catch (InvalidOperationException) { denied = true; }
            Require(denied, "Deletion publication accepted changed request/version.");
        }
        var nextStep = Guid.NewGuid();
        await using (var advance = new NpgsqlCommand("""
            UPDATE organization_deletion_progress SET phase='CARDS',step_id=@step,version=2 WHERE tenant_id=@tenant;
            UPDATE background_jobs SET state='SUCCEEDED' WHERE tenant_id=@tenant AND job_type='ORGANIZATION_DELETE_PAGE';
            """, admin))
        {
            advance.Parameters.AddWithValue("tenant", tenant); advance.Parameters.AddWithValue("step", nextStep);
            await advance.ExecuteNonQueryAsync(ct);
        }
        var completedReplay = await Replay(request);
        Require(completedReplay.Succeeded && !completedReplay.Value, "Replay requeued an acknowledged initial job.");
        await using (var verify = new NpgsqlCommand("""
            SELECT p.phase,p.step_id,p.version,j.state,j.correlation_id,r.correlation_id,j.safe_metadata->>'requestId',j.safe_metadata->>'stepId'
            FROM organization_deletion_progress p JOIN organization_deletion_requests r USING(tenant_id)
             JOIN background_jobs j USING(tenant_id) WHERE p.tenant_id=@tenant AND j.job_type='ORGANIZATION_DELETE_PAGE';
            """, admin))
        {
            verify.Parameters.AddWithValue("tenant", tenant);
            await using var rows = await verify.ExecuteReaderAsync(ct);
            Require(await rows.ReadAsync(ct) && rows.GetString(0) == "CARDS" && rows.GetGuid(1) == nextStep && rows.GetInt64(2) == 2
                && rows.GetString(3) == "SUCCEEDED" && rows.GetString(4) == "original-publication" && rows.GetString(5) == "original-publication"
                && rows.GetString(6) == request.ToString("D") && rows.GetString(7) == request.ToString("D"),
                "Replay reset progress, changed original attribution or rewrote job references.");
        }
        await AssertSnapshot("DELETING", 2, 1);
        Console.WriteLine("Deletion publication: owning/final-admission/queue-collision rollback, concurrent replay, request/version fences and retained progress passed.");
    }
    private sealed class AdmissionFixture : ICommandActorAuthorization
    {
        public bool RefuseFinal { get; set; }
        public int Calls { get; set; }
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(!RefuseFinal || ++Calls == 1);
        }
    }
}
