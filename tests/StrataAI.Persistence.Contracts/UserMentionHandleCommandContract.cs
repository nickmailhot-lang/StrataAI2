using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Identity;

// Real restricted PostgreSQL command composition. The caller's actor is a
// synthetic fixture; actual cookie expiry/revocation requires HTTP evidence.
internal static class UserMentionHandleCommandContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, CancellationToken ct)
    {
        var user = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash)
            VALUES(@user,@email,upper(@email),'Command account','ACTIVE',true,'fixture');
            """, admin))
        {
            seed.Parameters.AddWithValue("user", user); seed.Parameters.AddWithValue("email", $"handle-command-{user:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        var service = provider.GetRequiredService<UserMentionHandleService>();
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var identities = provider.GetRequiredService<IIdentityStore>();
        var receipts = provider.GetRequiredService<IIdentityHandleClaimReplayStore>();
        var initial = await service.GetAsync(user, ct);
        Require(initial.Succeeded && initial.Value is { UserVersion: 1, HandleVersion: 1 }
            && initial.Value.Handle == $"u_{user:N}", "Command did not admit its canonical account default.");
        var name = "command_" + user.ToString("N")[..16]; var key = Guid.NewGuid();
        var input = new ClaimMentionHandleInput("  " + name.ToUpperInvariant() + "  ", 1, 1);
        await using (var install = new NpgsqlCommand("""
            CREATE FUNCTION handle_contract_refuse_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'handle contract audit refusal'; END $$;
            CREATE TRIGGER handle_contract_refuse_audit BEFORE INSERT ON audit_events
            FOR EACH ROW WHEN (NEW.correlation_id = 'handle-contract-refused')
            EXECUTE FUNCTION handle_contract_refuse_audit();
            """, admin)) await install.ExecuteNonQueryAsync(ct);
        try
        {
            Require((await service.ClaimAsync(user, key, input, "handle-contract-refused", ct)).ErrorCode == "identity_storage_unavailable",
                "A real audit refusal was acknowledged as a committed handle claim.");
        }
        finally
        {
            await using var remove = new NpgsqlCommand("DROP TRIGGER handle_contract_refuse_audit ON audit_events; DROP FUNCTION handle_contract_refuse_audit();", admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        Require((await service.GetAsync(user, ct)).Value == initial.Value, "Audit failure retained a changed user/handle revision.");
        var restored = await unit.ExecuteAsync(user, async () =>
            IdentityOperation<IdentityHandleClaimReplay?>.Success(await receipts.ReadAsync(user, key, ct)), ct);
        Require(restored.Succeeded && restored.Value is null, "Audit failure retained the original retry receipt.");
        await using (var query = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM mention_handle_reservations WHERE user_id=@user)=1
              AND NOT EXISTS(SELECT 1 FROM identity_events WHERE user_id=@user)
              AND NOT EXISTS(SELECT 1 FROM audit_events WHERE actor_id=@user);
            """, admin))
        {
            query.Parameters.AddWithValue("user", user);
            Require(await query.ExecuteScalarAsync(ct) is true, "Audit failure leaked a reservation/event/audit.");
        }
        var changed = await service.ClaimAsync(user, key, input, "handle-contract-success", ct);
        Require(changed.Succeeded && changed.Value == new HandleClaimAcknowledgment(user, name, 2, 2, true),
            "Original failed key could not atomically commit its user/handle revisions.");
        Require((await service.ClaimAsync(user, key, input with { Handle = name }, "handle-contract-retry", ct)).Value == changed.Value,
            "Normalized original intent did not recover the original acknowledgment.");
        Require((await service.ClaimAsync(user, key, input with { Handle = "different" }, "handle-contract-retry", ct)).ErrorCode == "idempotency_key_reused",
            "Original key accepted different handle intent.");
        Require((await service.ClaimAsync(user, Guid.NewGuid(), input, "handle-contract-stale", ct)).ErrorCode == "version_conflict",
            "Fresh stale command was admitted.");
        var noop = await service.ClaimAsync(user, Guid.NewGuid(), new(name, 2, 2), "handle-contract-noop", ct);
        Require(noop.Succeeded && noop.Value == new HandleClaimAcknowledgment(user, name, 2, 2, false), "No-op advanced account identity.");
        await using (var query = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM identity_events WHERE user_id=@user)=1
              AND EXISTS(SELECT 1 FROM identity_events WHERE user_id=@user AND entity_version=2
                AND event_type='USER_PROFILE_UPDATED' AND metadata='{}'::jsonb)
              AND (SELECT count(*) FROM audit_events WHERE actor_id=@user)=1
              AND EXISTS(SELECT 1 FROM audit_events WHERE actor_id=@user AND tenant_id IS NULL
                AND entity_id=@user AND event_type='USER_PROFILE_UPDATED' AND safe_metadata='{}'::jsonb);
            """, admin))
        {
            query.Parameters.AddWithValue("user", user);
            Require(await query.ExecuteScalarAsync(ct) is true, "Retry/no-op duplicated events or copied handle text into audit/event metadata.");
        }
        var peer = await unit.ExecuteAsync(user, async () => IdentityOperation<UserIdentity?>.Success(
            await identities.UpdateProfileAsync(user, "Other profile edit", null, "en", "UTC", 2, DateTimeOffset.UtcNow, ct)), ct);
        Require(peer.Succeeded && peer.Value is { Version: 3 }, "Peer profile fixture failed.");
        Require((await service.ClaimAsync(user, key, input, "handle-contract-retry", ct)).Value == changed.Value,
            "An unrelated profile update invalidated unchanged handle receipt recovery.");
        Require((await service.ClaimAsync(user, Guid.NewGuid(), new("next_" + user.ToString("N"), 3, 2), "handle-contract-rename", ct)).Succeeded,
            "Later handle revision fixture failed.");
        Require((await service.ClaimAsync(user, key, input, "handle-contract-retry", ct)).ErrorCode == "mention_handle_unavailable",
            "An original receipt hydrated a former alias.");
        Require((await service.ClaimAsync(user, Guid.NewGuid(), new(name, 4, 3), "handle-contract-reclaim", ct)).Succeeded,
            "Same owner could not reclaim its reserved former alias.");
        Require((await service.ClaimAsync(user, key, input, "handle-contract-retry", ct)).ErrorCode == "mention_handle_unavailable",
            "Reclaiming the same name revived an obsolete receipt revision.");
        // Retain the disposable account with its append-only audit until this
        // isolated CI database is torn down; do not disable audit protection.
        Console.WriteLine("Restricted account handle command: atomic account/handle/audit/event/receipt, audit failure rollback, normalized original recovery, no-op and current-only hydration passed.");
    }
}
