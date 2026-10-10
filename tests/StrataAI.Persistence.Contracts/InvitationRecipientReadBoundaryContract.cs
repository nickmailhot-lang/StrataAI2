using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// PRD-03-TC-05 / PRD-03-TC-09 / PRD-60: real Worker authority delivery
// between actual reader admission and head reads must preserve owned continuity.
internal static class InvitationRecipientReadBoundaryContract
{
    private sealed class Context : ICommandActorContext
    {
        public bool HasHttpRequest => true;
        public Guid? AuthenticatedUserId { get; set; }
        public string? SessionTokenHash { get; set; }
    }
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var owner = Guid.NewGuid(); var actor = Guid.NewGuid(); var tenant = Guid.NewGuid(); var board = Guid.NewGuid();
        var email = $"authority-ready-{actor:N}@example.test";
        var ownerHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var actorHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Recipient','ACTIVE',true,'fixture',now(),now()),
              (@owner,'owner-'||@owner::text||'@example.test',upper('owner-'||@owner::text||'@example.test'),'Owner','ACTIVE',true,'fixture',now(),now());
            INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at)
             VALUES(gen_random_uuid(),@actor,@actor_hash,now(),now()+interval '1 hour'),
              (gen_random_uuid(),@owner,@owner_hash,now(),now()+interval '1 hour');
            INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at)
             VALUES(@tenant,'Authority readiness scope',@owner,'ACTIVE',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@owner,'OWNER','ACTIVE'),(gen_random_uuid(),@tenant,@actor,'MEMBER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,lifecycle_state,created_at,updated_at)
             VALUES(@board,@tenant,'Authority readiness Board','ACTIVE',now(),now());
            INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
             VALUES(gen_random_uuid(),@tenant,@board,@owner,'ADMIN','ACTIVE',now(),now());
            """, admin))
        {
            foreach (var pair in new Dictionary<string, object> { ["actor"] = actor, ["owner"] = owner, ["tenant"] = tenant,
                ["board"] = board, ["email"] = email, ["actor_hash"] = actorHash, ["owner_hash"] = ownerHash })
                seed.Parameters.AddWithValue(pair.Key, pair.Value);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var context = new Context { AuthenticatedUserId = owner, SessionTokenHash = ownerHash };
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ICommandActorContext>(context); services.AddSingleton<ICommandActorAuthorization, CommandActorAuthorization>();
        services.AddSingleton<PostgresBackgroundJobStore>(); services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract", ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }) }).Build();
        services.AddStrataAiIdentity(settings, runtime); services.AddStrataAiOrganizations(runtime);
        services.AddStrataAiWorkManagement(runtime); services.AddStrataAiOnboarding(runtime, settings);
        services.AddSingleton<IInvitationRecipientEventReader>(p => new BoundaryReader(
            new PostgresInvitationRecipientEventReader(p.GetRequiredService<PostgresConnectionFactory>(),
                p.GetRequiredService<IIdentityStore>()), workerConnection, tenant));
        await using var provider = services.BuildServiceProvider();
        var created = await provider.GetRequiredService<BoardInvitationService>().CreateAsync(board, owner, email,
            BoardRole.Member, "readiness-create", ct);
        Require(created.Succeeded, "Actual Board invitation creation failed.");
        context.AuthenticatedUserId = actor; context.SessionTokenHash = actorHash;
        var replay = provider.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var checkpoint = await replay.ReadAsync(actor, null, cancellationToken: ct);
        Require(checkpoint.Succeeded && checkpoint.Value is { ResetRequired: true, Events.Count: 0 }, "Initial actual recipient checkpoint failed.");
        var accepted = await provider.GetRequiredService<IInvitationService>().AcceptPendingAsync(actor,
            created.Value!.Invitation.Id, "readiness-accept", ct);
        Require(accepted.Succeeded, "Actual first Board grant acceptance failed.");
        var boundary = (BoundaryReader)provider.GetRequiredService<IInvitationRecipientEventReader>();
        boundary.Armed = true;
        var changed = await replay.ReadAsync(actor, checkpoint.Value!.Cursor, cancellationToken: ct);
        Require(boundary.Delivered, "Actual Worker delivery did not execute at the read boundary.");
        Require(!changed.Succeeded && changed.ErrorCode == "account_unavailable" && changed.Value is null,
            "Read-boundary admission change did not produce a structured failure.");
        var recovery = await replay.RecoverLiveReadAsync(actor, checkpoint.Value.Cursor, ct);
        Require(recovery.Succeeded && recovery.Value is { ResetRequired: true, Events.Count: 0 },
            "Actual authority-only read boundary did not recover the owned checkpoint.");
        var acceptedSource = await replay.ReadAsync(actor, recovery.Value!.Cursor, cancellationToken: ct);
        Require(acceptedSource.Succeeded && acceptedSource.Value is { ResetRequired: false, Events.Count: 1 }
            && acceptedSource.Value.Events[0].EventType == "INVITATION_ACCEPTED",
            "Actual accepted source did not recover after the read boundary.");
        await using (var original = new NpgsqlCommand("SELECT event_id,created_at FROM invitation_recipient_events WHERE tenant_id=@tenant AND entity_id=@invitation AND event_type='INVITATION_ACCEPTED';", admin))
        {
            original.Parameters.AddWithValue("tenant", tenant); original.Parameters.AddWithValue("invitation", created.Value.Invitation.Id);
            await using var row = await original.ExecuteReaderAsync(ct);
            Require(await row.ReadAsync(ct) && acceptedSource.Value!.Events[0].EventId == row.GetGuid(0)
                && acceptedSource.Value.Events[0].CreatedAt == row.GetFieldValue<DateTimeOffset>(1),
                "Recovered accepted event identity or original timestamp changed.");
            Require(!await row.ReadAsync(ct), "Accepted event appeared more than once in actual history.");
        }
        var oldCurrent = await replay.IsCursorCurrentAsync(actor, checkpoint.Value.Cursor, ct);
        Require(oldCurrent.Succeeded && !oldCurrent.Value, "Old admission cursor remained current after actual Worker delivery.");
        var next = await replay.ReadAsync(actor, acceptedSource.Value!.Cursor, cancellationToken: ct);
        Require(next.Succeeded && next.Value is { ResetRequired: false, Events.Count: 0 },
            "Recovered actual accepted source was duplicated.");
    }
    private sealed class BoundaryReader(IInvitationRecipientEventReader inner, string workerConnection, Guid tenant)
        : IInvitationRecipientEventReader
    {
        public bool Armed { get; set; }
        public bool Delivered { get; private set; }
        public async Task<InvitationRecipientCursorBinding?> GetScopeAsync(Guid actor, CancellationToken ct)
        {
            var scope = await inner.GetScopeAsync(actor, ct);
            if (Armed && !Delivered && scope is not null)
            {
                Armed = false;
                await using var worker = new PostgresConnectionFactory(workerConnection);
                var jobs = new PostgresBackgroundJobStore(worker, authorityJobsOnly: true);
                var job = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct)
                    ?? throw new InvalidOperationException("Actual authority job absent at boundary.");
                await new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker)).ExecuteAsync(job, ct);
                Require(await jobs.CompleteAsync(tenant, job.Id, job.LeaseId, job.WorkerId, ct), "Actual boundary Worker delivery acknowledgment failed.");
                Delivered = true;
            }
            return scope;
        }
        public Task<long> GetHeadAsync(InvitationRecipientCursorBinding binding, CancellationToken ct) => inner.GetHeadAsync(binding, ct);
        public Task<InvitationRecipientEventWindow> ReadAsync(InvitationRecipientCursorBinding binding, long since, int limit, CancellationToken ct)
            => inner.ReadAsync(binding, since, limit, ct);
    }
}
