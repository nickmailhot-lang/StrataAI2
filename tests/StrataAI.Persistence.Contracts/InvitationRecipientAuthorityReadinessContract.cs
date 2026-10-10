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

internal static class InvitationRecipientAuthorityReadinessContract
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
        var waiting = await replay.ReadAsync(actor, checkpoint.Value!.Cursor, cancellationToken: ct);
        Require(waiting.Succeeded && waiting.Value is { ResetRequired: false, Events.Count: 0, HasMore: false },
            "Accepted source escaped before its actual membership authority delivery.");
        Guid actualSource;
        await using (var dependency = new NpgsqlCommand("""
            SELECT d.source_event_id FROM invitation_recipient_authority_dependencies d
             JOIN invitation_recipient_events e ON (e.tenant_id,e.event_id)=(d.tenant_id,d.recipient_event_id)
             WHERE e.tenant_id=@tenant AND e.entity_id=@invitation AND e.event_type='INVITATION_ACCEPTED';
            """, admin))
        {
            dependency.Parameters.AddWithValue("tenant", tenant);
            dependency.Parameters.AddWithValue("invitation", created.Value.Invitation.Id);
            actualSource = await dependency.ExecuteScalarAsync(ct) is Guid sourceId ? sourceId
                : throw new InvalidOperationException("Actual acceptance causal dependency was absent.");
        }
        await using (var api = new NpgsqlConnection(apiConnection))
        {
            await api.OpenAsync(ct);
            await using (var scope = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,false);", api))
            { scope.Parameters.AddWithValue("tenant", tenant.ToString()); await scope.ExecuteScalarAsync(ct); }
            foreach (var attempt in new[] {
                (Tenant: tenant, Actor: actor, Invitation: created.Value.Invitation.Id, Source: actualSource),
                (Tenant: Guid.NewGuid(), Actor: actor, Invitation: created.Value.Invitation.Id, Source: actualSource),
                (Tenant: tenant, Actor: owner, Invitation: created.Value.Invitation.Id, Source: actualSource),
                (Tenant: tenant, Actor: actor, Invitation: Guid.NewGuid(), Source: actualSource),
                (Tenant: tenant, Actor: actor, Invitation: created.Value.Invitation.Id, Source: Guid.NewGuid()) })
            {
                await using var bind = new NpgsqlCommand("SELECT bind_invitation_recipient_authority_dependency(@tenant,@actor,@invitation,@source);", api);
                bind.Parameters.AddWithValue("tenant", attempt.Tenant); bind.Parameters.AddWithValue("actor", attempt.Actor);
                bind.Parameters.AddWithValue("invitation", attempt.Invitation); bind.Parameters.AddWithValue("source", attempt.Source);
                Require(await bind.ExecuteScalarAsync(ct) is false, "Committed or foreign source acquired a new dependency.");
            }
            await using var privateRead = new NpgsqlCommand("SELECT * FROM invitation_recipient_authority_dependencies;", api);
            try
            {
                await privateRead.ExecuteNonQueryAsync(ct);
                throw new InvalidOperationException("API acquired private dependency history.");
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using (var privileges = new NpgsqlCommand("""
            SELECT NOT has_function_privilege('strataai_worker_runtime','bind_invitation_recipient_authority_dependency(uuid,uuid,uuid,uuid)','EXECUTE')
             AND NOT has_function_privilege('strataai_worker_runtime','invitation_recipient_event_ready(bigint)','EXECUTE')
             AND NOT has_table_privilege('strataai_api_runtime','invitation_recipient_authority_dependencies','SELECT')
             AND NOT has_table_privilege('strataai_worker_runtime','invitation_recipient_authority_dependencies','SELECT');
            """, admin))
            Require(await privileges.ExecuteScalarAsync(ct) is true, "Recipient dependency capability widened runtime grants.");
        context.AuthenticatedUserId = owner; context.SessionTokenHash = ownerHash;
        var later = await provider.GetRequiredService<IInvitationService>().CreateAsync(tenant, owner, email,
            InvitationSurface.Portal, "OWNER", "readiness-accept", ct);
        Require(later.Succeeded, "Actual later recipient source creation failed.");
        context.AuthenticatedUserId = actor; context.SessionTokenHash = actorHash;
        var held = await replay.ReadAsync(actor, checkpoint.Value.Cursor, cancellationToken: ct);
        Require(held.Succeeded && held.Value is { ResetRequired: false, Events.Count: 0, HasMore: false },
            "Later source bypassed the pending actual acceptance dependency.");
        await using (var worker = new PostgresConnectionFactory(workerConnection))
        {
            var jobs = new PostgresBackgroundJobStore(worker, authorityJobsOnly: true);
            var job = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct)
                ?? throw new InvalidOperationException("Actual accepted membership authority source was not claimed.");
            await new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker)).ExecuteAsync(job, ct);
            Require(await jobs.CompleteAsync(tenant, job.Id, job.LeaseId, job.WorkerId, ct), "Actual authority delivery acknowledgment failed.");
        }
        var reset = await replay.ReadAsync(actor, checkpoint.Value.Cursor, cancellationToken: ct);
        Require(reset.Succeeded && reset.Value is { ResetRequired: true, Events.Count: 0 }, "Delivered actual authority did not reset the old binding.");
        var recovered = await replay.RecoverLiveCheckpointAsync(actor, checkpoint.Value.Cursor, reset.Value!.Cursor, ct);
        Require(recovered.Succeeded && recovered.Value is not null, "Admitted live checkpoint could not rebind after delivery.");
        var source = await replay.ReadAsync(actor, recovered.Value, cancellationToken: ct);
        Require(source.Succeeded && source.Value is { ResetRequired: false, Events.Count: 2, HasMore: false }
            && source.Value.Events[0].EventType == "INVITATION_ACCEPTED"
            && source.Value.Events[1].EventType == "INVITATION_CREATED"
            && source.Value.Events[1].Sequence == source.Value.Events[0].Sequence + 1,
            "Actual accepted and later sources were not delivered in order after authority readiness.");
        // The recipient journal records the proven transition's canonical
        // updated_at, as defined by migrations 102/104, rather than the earlier
        // application-supplied acceptance command time.
        await using var check = new NpgsqlCommand("SELECT updated_at FROM invitations WHERE tenant_id=@tenant AND id=@id;", admin);
        check.Parameters.AddWithValue("tenant", tenant); check.Parameters.AddWithValue("id", created.Value.Invitation.Id);
        await using (var row = await check.ExecuteReaderAsync(ct))
        {
            Require(await row.ReadAsync(ct), "Canonical accepted invitation was absent.");
            Require(source.Value!.Events[0].CreatedAt == await row.GetFieldValueAsync<DateTimeOffset>(0, ct),
                "Recovered source lost its actual canonical timestamp.");
        }
        var again = await replay.ReadAsync(actor, source.Value.Cursor, cancellationToken: ct);
        Require(again.Succeeded && again.Value is { ResetRequired: false, Events.Count: 0 }, "Delivered acceptance source repeated.");
        Console.WriteLine("Recipient authority readiness: actual restricted acceptance remains pending until leased membership delivery, then protected reset/checkpoint recovery delivers canonical source once.");
    }
}
