using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Session admission is synthetic here; real HTTP/session evidence belongs to
// the exact-image fixture. Queries and owning read transactions are real.
internal static class OrganizationMetadataReplayContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var membership = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Replay Owner','ACTIVE','unused-contract-hash',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Private replay name',@actor,'ACTIVE',1,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(@member,@tenant,@actor,'OWNER','ACTIVE');
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
             VALUES(@first,@tenant,@actor,'ORGANIZATION_CREATED','Organization',@tenant,'replay-contract','{}');
            UPDATE organizations SET name='Private later name',version=2,updated_at=clock_timestamp() WHERE id=@tenant;
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
             VALUES(@second,@tenant,@actor,'ORGANIZATION_UPDATED','Organization',@tenant,'replay-contract','{}');
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"metadata-replay-{actor:N}@example.test");
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("member", membership);
            seed.Parameters.AddWithValue("first", first); seed.Parameters.AddWithValue("second", second); await seed.ExecuteNonQueryAsync(ct);
        }
        var admission = new Admission();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ICommandActorAuthorization>(admission);
        services.AddSingleton(new IdentityPolicy(true, false, 12, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var connections = provider.GetRequiredService<PostgresConnectionFactory>();
        var reader = provider.GetRequiredService<IOrganizationMetadataEventReader>();
        var codec = provider.GetRequiredService<IOrganizationMetadataCursorCodec>();
        var replay = provider.GetRequiredService<TransactionalOrganizationMetadataSynchronization>();
        var binding = new OrganizationMetadataCursorBinding(tenant, actor, membership, 1);
        var cursor = codec.Encode(binding, 0);
        try { await reader.GetHeadAsync(tenant, ct); throw new InvalidOperationException("Standalone metadata replay was admitted."); }
        catch (InvalidOperationException error) when (error.Message == "Metadata replay requires its owning tenant read transaction.") { }
        var pending = await replay.ReadAsync(tenant, actor, cursor, 1, ct);
        Require(pending.Succeeded && pending.Value is { Pending: true, ResetRequired: false, Events.Count: 0 }, "Pending metadata source was skipped.");
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var jobs = new PostgresBackgroundJobStore(worker, metadataJobsOnly: true);
        var claim1 = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Replay delivery source missing.");
        var claim2 = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct) ?? throw new InvalidOperationException("Replay second delivery source missing.");
        var delivery = new OrganizationMetadataDeliveryHandler(new PostgresOrganizationMetadataDeliveryStore(worker));
        await delivery.ExecuteAsync(claim2, ct);
        pending = await replay.ReadAsync(tenant, actor, cursor, 1, ct);
        Require(pending.Succeeded && pending.Value is { Pending: true, Events.Count: 0 }, "Later ready metadata event bypassed an earlier pending source.");
        await delivery.ExecuteAsync(claim1, ct);
        foreach (var claim in new[] { claim1, claim2 }) Require(await jobs.CompleteAsync(tenant, claim.Id, claim.LeaseId, claim.WorkerId, ct), "Replay source acknowledgment failed.");
        var page1 = await replay.ReadAsync(tenant, actor, cursor, 1, ct);
        Require(page1.Succeeded && page1.Value is { HasMore: true, Events.Count: 1 } && page1.Value.Events[0].EventId == first,
            "Metadata replay did not return the bounded first canonical source.");
        var page2 = await replay.ReadAsync(tenant, actor, page1.Value!.Cursor, 1, ct);
        Require(page2.Succeeded && page2.Value is { HasMore: false, Events.Count: 1 } && page2.Value.Events[0].EventId == second
            && page2.Value.Events[0].Metadata.Count == 0 && page2.Value.Events[0].BoardId is null, "Metadata replay exposed content or lost the second source.");
        admission.RejectFinal = true;
        var refused = await replay.ReadAsync(tenant, actor, cursor, 1, ct);
        Require(!refused.Succeeded && refused.ErrorCode == "session_unavailable" && refused.Value is null, "Final session refusal retained metadata payload/cursor.");
        admission.RejectFinal = false;
        await using (var remove = new NpgsqlCommand("UPDATE organization_members SET status='REMOVED',version=version+1 WHERE id=@member", admin))
        { remove.Parameters.AddWithValue("member", membership); await remove.ExecuteNonQueryAsync(ct); }
        refused = await replay.ReadAsync(tenant, actor, cursor, 1, ct);
        Require(!refused.Succeeded && refused.Value is null, "Removed membership retained metadata replay.");
        await using (var restore = new NpgsqlCommand("UPDATE organization_members SET status='ACTIVE',version=version+1 WHERE id=@member", admin))
        { restore.Parameters.AddWithValue("member", membership); await restore.ExecuteNonQueryAsync(ct); }
        var reset = await replay.ReadAsync(tenant, actor, cursor, 1, ct);
        Require(reset.Succeeded && reset.Value is { ResetRequired: true, Events.Count: 0 }, "Restored membership reused its old cursor authority.");
        Console.WriteLine("Organization metadata replay: owning transaction, contiguous ready prefix, bounded paging, opaque source identity, final session refusal, membership withdrawal and restored-cursor reset passed.");
    }
    private sealed class Admission : ICommandActorAuthorization
    {
        public bool RejectFinal { get; set; }
        private int _checks;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(!RejectFinal || ++_checks % 2 == 1);
    }
}
