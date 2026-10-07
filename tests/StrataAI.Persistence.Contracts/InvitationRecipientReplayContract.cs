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
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Real restricted API login and persisted session admission. The request context
// is synthetic; cookie/SignalR/browser delivery remains a separate acceptance gate.
internal static class InvitationRecipientReplayContract
{
    private sealed class Context(Guid actor, string hash) : ICommandActorContext
    {
        public bool HasHttpRequest => true;
        public Guid? AuthenticatedUserId { get; set; } = actor;
        public string? SessionTokenHash => hash;
    }
    private sealed class BoundaryReader(IInvitationRecipientEventReader inner) : IInvitationRecipientEventReader
    {
        public Action? AfterRead { get; set; }
        public Task<InvitationRecipientCursorBinding?> GetScopeAsync(Guid actor, CancellationToken ct) => inner.GetScopeAsync(actor, ct);
        public Task<long> GetHeadAsync(InvitationRecipientCursorBinding binding, CancellationToken ct) => inner.GetHeadAsync(binding, ct);
        public async Task<InvitationRecipientEventWindow> ReadAsync(InvitationRecipientCursorBinding binding, long since, int limit, CancellationToken ct)
        { var result = await inner.ReadAsync(binding, since, limit, ct); AfterRead?.Invoke(); return result; }
    }
    private static void Require(bool value, string invariant)
    { if (!value) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var actor = Guid.NewGuid(); var owner = Guid.NewGuid(); var tenant = Guid.NewGuid(); var portalTenant = Guid.NewGuid();
        var email = $"replay-{actor:N}@example.test"; var hash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var firstAudit = Guid.NewGuid(); var secondAudit = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Recipient','ACTIVE',true,'fixture',now(),now()),
              (@owner,'owner-'||@owner::text||'@example.test',upper('owner-'||@owner::text||'@example.test'),'Owner','ACTIVE',true,'fixture',now(),now());
            INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at) VALUES(gen_random_uuid(),@actor,@hash,now(),now()+interval '1 hour');
            INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at)
             VALUES(@tenant,'Private Internal source',@owner,'ACTIVE',now(),now()),(@portal,'Private Portal source',@owner,'ACTIVE',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@owner,'OWNER','ACTIVE'),(gen_random_uuid(),@portal,@owner,'OWNER','ACTIVE');
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             VALUES(@first,@tenant,@email,upper(@email),encode(sha256(@first::text::bytea),'hex'),'INTERNAL','MEMBER',@owner,clock_timestamp(),now()+interval '1 day'),
              (@second,@portal,@email,upper(@email),encode(sha256(@second::text::bytea),'hex'),'PORTAL','OWNER',@owner,clock_timestamp(),now()+interval '1 day');
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
             VALUES(@first_audit,@tenant,@owner,'ORGANIZATION_MEMBER_INVITED','Invitation',@first,'recipient-replay-contract'),
              (@second_audit,@portal,@owner,'ORGANIZATION_MEMBER_INVITED','Invitation',@second,'recipient-replay-contract');
            """, admin))
        {
            foreach (var parameter in new Dictionary<string, object> { ["actor"] = actor, ["owner"] = owner, ["tenant"] = tenant,
                ["portal"] = portalTenant, ["email"] = email, ["hash"] = hash, ["first"] = first, ["second"] = second,
                ["first_audit"] = firstAudit, ["second_audit"] = secondAudit }) seed.Parameters.AddWithValue(parameter.Key, parameter.Value);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        var context = new Context(actor, hash); services.AddSingleton<ICommandActorContext>(context);
        services.AddSingleton<ICommandActorAuthorization, CommandActorAuthorization>();
        services.AddSingleton<PostgresBackgroundJobStore>(); services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract", ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }) }).Build();
        services.AddStrataAiIdentity(settings, runtime); services.AddStrataAiOrganizations(runtime);
        services.AddStrataAiWorkManagement(runtime); services.AddStrataAiOnboarding(runtime, settings);
        services.AddSingleton<BoundaryReader>(p => new(ActivatorUtilities.CreateInstance<PostgresInvitationRecipientEventReader>(p)));
        services.AddSingleton<IInvitationRecipientEventReader>(p => p.GetRequiredService<BoundaryReader>());
        await using var provider = services.BuildServiceProvider();
        var reader = provider.GetRequiredService<IInvitationRecipientEventReader>();
        var replay = provider.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var codec = provider.GetRequiredService<IInvitationRecipientCursorCodec>();
        var binding = new InvitationRecipientCursorBinding(actor, email.ToUpperInvariant(), 1);
        try { await reader.GetScopeAsync(actor, ct); throw new InvalidOperationException("Unowned recipient read succeeded."); }
        catch (InvalidOperationException error) when (error.Message == "Recipient replay requires its owning account transaction.") { }
        var bootstrap = await replay.ReadAsync(actor, null, cancellationToken: ct);
        Require(bootstrap.Succeeded && bootstrap.Value is { ResetRequired: true, Events.Count: 0 }
            && codec.TryDecode(binding, bootstrap.Value.Cursor, out var head) && head == 2, "Bootstrap did not capture current recipient head.");
        var page = await replay.ReadAsync(actor, codec.Encode(binding, 0), 1, ct);
        Require(page.Succeeded && page.Value is { HasMore: true, ResetRequired: false, Events.Count: 1 }
            && page.Value.Events[0].EventId == firstAudit && page.Value.Events[0].Sequence == 1, "First actual source page lost identity/order.");
        var final = await replay.ReadAsync(actor, page.Value!.Cursor, 1, ct);
        Require(final.Succeeded && final.Value is { HasMore: false, Events.Count: 1 }
            && final.Value.Events[0].EventId == secondAudit && final.Value.Events[0].Sequence == 2, "Cross-Organization Portal replay lost canonical source.");
        var revokedAudit = Guid.NewGuid();
        await using (var revokeSource = new NpgsqlCommand("""
            BEGIN;
            UPDATE invitations SET revoked_at=clock_timestamp() WHERE id=@first;
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
             VALUES(@audit,@tenant,@owner,'INVITATION_REVOKED','Invitation',@first,'recipient-replay-revoke');
            COMMIT;
            """, admin))
        {
            revokeSource.Parameters.AddWithValue("first", first); revokeSource.Parameters.AddWithValue("audit", revokedAudit);
            revokeSource.Parameters.AddWithValue("tenant", tenant); revokeSource.Parameters.AddWithValue("owner", owner);
            await revokeSource.ExecuteNonQueryAsync(ct);
        }
        var missed = await replay.ReadAsync(actor, bootstrap.Value!.Cursor, cancellationToken: ct);
        Require(missed.Succeeded && missed.Value is { ResetRequired: false, HasMore: false, Events.Count: 1 }
            && missed.Value.Events[0].EventId == revokedAudit && missed.Value.Events[0].EventType == "INVITATION_REVOKED"
            && missed.Value.Events[0].Sequence == 3, "Original bootstrap cursor missed a later actual source mutation.");
        var repeated = await replay.ReadAsync(actor, missed.Value!.Cursor, cancellationToken: ct);
        Require(repeated.Succeeded && repeated.Value is { ResetRequired: false, HasMore: false, Events.Count: 0 }, "Acknowledged source was replayed twice.");
        Require((await replay.IsCursorCurrentAsync(actor, missed.Value.Cursor, ct)).Value, "Current account cursor was refused.");
        var boundary = provider.GetRequiredService<BoundaryReader>();
        boundary.AfterRead = () => context.AuthenticatedUserId = owner;
        var withdrawn = await replay.ReadAsync(actor, bootstrap.Value.Cursor, cancellationToken: ct);
        Require(!withdrawn.Succeeded && withdrawn.Value is null && withdrawn.ErrorCode == "session_unavailable",
            "Request actor withdrawal after actual source I/O disclosed a replay page.");
        boundary.AfterRead = null; context.AuthenticatedUserId = actor;
        await using (var noGrant = new NpgsqlCommand("SELECT count(*) FROM organization_members WHERE user_id=@actor;", admin))
        { noGrant.Parameters.AddWithValue("actor", actor); Require((long)(await noGrant.ExecuteScalarAsync(ct))! == 0, "Recipient replay manufactured Internal membership."); }
        await PreservedBoardAdminAsync(admin, provider, tenant, owner, actor, email, ct);
        context.AuthenticatedUserId = owner;
        var wrongSession = await replay.ReadAsync(actor, final.Value!.Cursor, cancellationToken: ct);
        Require(!wrongSession.Succeeded && wrongSession.Value is null, "Switched request actor disclosed replay.");
        context.AuthenticatedUserId = actor;
        await using (var update = new NpgsqlCommand("UPDATE users SET email=@email,email_normalized=upper(@email),version=version+1 WHERE id=@actor;", admin))
        { update.Parameters.AddWithValue("actor", actor); update.Parameters.AddWithValue("email", "changed-" + email); await update.ExecuteNonQueryAsync(ct); }
        var changed = await replay.ReadAsync(actor, final.Value.Cursor, cancellationToken: ct);
        Require(changed.Succeeded && changed.Value is { ResetRequired: true, Events.Count: 0 }, "Old email/revision cursor disclosed previous recipient history.");
        Require(!(await replay.IsCursorCurrentAsync(actor, missed.Value.Cursor, ct)).Value, "Old account cursor survived final authority check.");
        await using (var unverify = new NpgsqlCommand("UPDATE users SET email_verified=false,version=version+1 WHERE id=@actor;", admin))
        { unverify.Parameters.AddWithValue("actor", actor); await unverify.ExecuteNonQueryAsync(ct); }
        var denied = await replay.ReadAsync(actor, changed.Value!.Cursor, cancellationToken: ct);
        Require(!denied.Succeeded && denied.Value is null, "Unverified account disclosed replay.");
        await using (var revoke = new NpgsqlCommand("UPDATE users SET email_verified=true,version=version+1 WHERE id=@actor; UPDATE sessions SET revoked_at=clock_timestamp() WHERE token_hash=@hash;", admin))
        { revoke.Parameters.AddWithValue("actor", actor); revoke.Parameters.AddWithValue("hash", hash); await revoke.ExecuteNonQueryAsync(ct); }
        denied = await replay.ReadAsync(actor, null, cancellationToken: ct);
        Require(!denied.Succeeded && denied.Value is null, "Revoked persisted session disclosed replay.");
        Console.WriteLine("Recipient replay: restricted API login, owning account scope, bounded cross-Organization order, canonical identities, later mutation recovery, email/revision reset, actor withdrawal before/after source I/O, unverified account and persisted-session revocation passed.");
    }
    private static async Task PreservedBoardAdminAsync(NpgsqlConnection admin, ServiceProvider provider, Guid tenant,
        Guid owner, Guid actor, string email, CancellationToken ct)
    {
        var board = Guid.NewGuid(); var invitation = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            BEGIN;
            INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@tenant,@actor,'MEMBER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,lifecycle_state,created_at,updated_at) VALUES(@board,@tenant,'Existing Admin Board','ACTIVE',now(),now());
            INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
             VALUES(gen_random_uuid(),@tenant,@board,@actor,'ADMIN','ACTIVE',now(),now());
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
             target_board_id,target_board_role,created_by_user_id,created_at,expires_at)
             VALUES(@invitation,@tenant,@email,upper(@email),encode(sha256(@invitation::text::bytea),'hex'),'INTERNAL','MEMBER',
              @board,'MEMBER',@owner,clock_timestamp(),now()+interval '1 day');
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
             VALUES(gen_random_uuid(),@tenant,@owner,'BOARD_MEMBER_INVITED','Invitation',@invitation,'preserved-admin-create');
            COMMIT;
            """, admin))
        {
            foreach (var parameter in new Dictionary<string, object> { ["tenant"] = tenant, ["actor"] = actor, ["owner"] = owner,
                ["email"] = email, ["board"] = board, ["invitation"] = invitation }) seed.Parameters.AddWithValue(parameter.Key, parameter.Value);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var service = provider.GetRequiredService<IInvitationService>();
        var accepted = await service.AcceptPendingAsync(actor, invitation, "preserved-admin-accept", ct);
        Require(accepted.Succeeded, "Existing Board Admin could not accept a Member invitation with its retained canonical grant: " + accepted.ErrorCode);
        await using (var check = new NpgsqlCommand("""
            SELECT m.role='ADMIN' AND m.status='ACTIVE' AND i.target_board_role='MEMBER' AND i.accepted_by_user_id=@actor
             AND (SELECT count(*) FROM invitation_recipient_events e JOIN audit_events a ON a.id=e.event_id
              WHERE e.entity_id=i.id AND e.tenant_id=i.tenant_id AND e.event_type='INVITATION_ACCEPTED'
               AND e.actor_id=@actor AND a.actor_id=@actor AND e.entity_version=i.version AND e.created_at=i.updated_at)=1
             FROM invitations i JOIN board_members m ON m.tenant_id=i.tenant_id AND m.board_id=i.target_board_id AND m.user_id=@actor
             WHERE i.id=@invitation AND i.tenant_id=@tenant;
            """, admin))
        {
            check.Parameters.AddWithValue("actor", actor); check.Parameters.AddWithValue("invitation", invitation); check.Parameters.AddWithValue("tenant", tenant);
            Require(await check.ExecuteScalarAsync(ct) is true, "Retained Board Admin acceptance lost canonical grant or original source proof.");
        }
        var retry = await service.AcceptPendingAsync(actor, invitation, "preserved-admin-retry", ct);
        Require(retry.Succeeded, "Original accepted invitation recovery failed.");
        await using var count = new NpgsqlCommand("SELECT count(*) FROM invitation_recipient_events WHERE tenant_id=@tenant AND entity_id=@invitation;", admin);
        count.Parameters.AddWithValue("tenant", tenant); count.Parameters.AddWithValue("invitation", invitation);
        Require((long)(await count.ExecuteScalarAsync(ct))! == 2, "Accepted invitation recovery published duplicate source history.");
        Console.WriteLine("Retained Board Admin: actual restricted acceptance service preserves Admin for Member target, publishes one canonical accepted source and retries without duplicate history.");
    }
}
