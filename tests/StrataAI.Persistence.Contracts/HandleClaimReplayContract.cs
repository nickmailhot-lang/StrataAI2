using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Identity;

// Real restricted Production adapter/owning unit; actor eligibility is the
// caller's synthetic fixture, not HTTP/current-handle hydration acceptance.
internal static class HandleClaimReplayContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid user, Guid foreign, CancellationToken ct)
    {
        var store = provider.GetRequiredService<IIdentityHandleClaimReplayStore>(); var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var key = Guid.NewGuid(); var expiredKey = Guid.NewGuid(); var rollbackKey = Guid.NewGuid();
        var fingerprint = new string('a', 64);
        var handle = await unit.ExecuteAsync(user, async () => IdentityOperation<UserMentionHandle?>.Success(
            await provider.GetRequiredService<IUserMentionHandleStore>().FindAsync(user, ct)), ct);
        Require(handle.Succeeded && handle.Value is not null, "Receipt fixture current handle was unavailable.");
        var receipt = new HandleClaimReceipt(1, handle.Value!.Version, true);
        async Task<IdentityHandleClaimReplay?> Read(Guid lookupKey)
        {
            var result = await unit.ExecuteAsync(user, async () => IdentityOperation<IdentityHandleClaimReplay?>.Success(
                await store.ReadAsync(user, lookupKey, ct)), ct);
            Require(result.Succeeded, "Receipt owning transaction failed."); return result.Value;
        }
        try
        {
            try { await store.ReadAsync(user, key, ct); throw new InvalidOperationException("Unowned receipt read was accepted."); }
            catch (InvalidOperationException error) when (error.Message == "Handle claim retries require an owning identity subject transaction.") { }
            var stored = await unit.ExecuteAsync(user, async () =>
            {
                try { await store.ReadAsync(foreign, key, ct); throw new InvalidOperationException("Foreign receipt read accepted."); }
                catch (InvalidOperationException error) when (error.Message == "Handle claim retries require an owning identity subject transaction.") { }
                try { await store.TrySaveAsync(foreign, key, fingerprint, receipt, ct); throw new InvalidOperationException("Foreign receipt insert accepted."); }
                catch (InvalidOperationException error) when (error.Message == "Handle claim retries require an owning identity subject transaction.") { }
                return IdentityOperation<bool>.Success(await store.TrySaveAsync(user, key, fingerprint, receipt, ct));
            }, ct);
            Require(stored is { Succeeded: true, Value: true }, "First original receipt was not stored.");
            var current = await Read(key);
            Require(current is { Expired: false } && current.Fingerprint == fingerprint && current.Receipt == receipt
                && current.ExpiresAt - current.CreatedAt == TimeSpan.FromHours(24), "Receipt adapter lost immutable metadata or exact retention.");
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(current!.Receipt)))
                Require(payload.RootElement.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "Changed", "HandleVersion", "UserVersion" }),
                    "Actual acknowledgment contains former handle/profile/credential text.");
            var duplicate = await unit.ExecuteAsync(user, async () => IdentityOperation<bool>.Success(
                await store.TrySaveAsync(user, key, new string('b', 64), new(2, 2, false), ct)), ct);
            Require(duplicate is { Succeeded: true, Value: false } && await Read(key) == current,
                "Original retry identity was overwritten or extended.");
            var refused = await unit.ExecuteAsync<bool>(user, async () =>
            {
                Require(await store.TrySaveAsync(user, rollbackKey, fingerprint, receipt, ct), "Receipt rollback setup failed.");
                return IdentityOperation<bool>.Failure("fixture_refused");
            }, ct);
            Require(refused.ErrorCode == "fixture_refused" && await Read(rollbackKey) is null,
                "Receipt survived owning transaction refusal.");
            await using (var seedExpired = new NpgsqlCommand("""
                INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed,created_at,updated_at,expires_at)
                VALUES(@user,@key,@fingerprint,1,1,false,statement_timestamp()-interval '2 days',statement_timestamp()-interval '2 days',statement_timestamp()-interval '1 day');
                """, admin))
            {
                seedExpired.Parameters.AddWithValue("user", user); seedExpired.Parameters.AddWithValue("key", expiredKey);
                seedExpired.Parameters.AddWithValue("fingerprint", fingerprint); await seedExpired.ExecuteNonQueryAsync(ct);
            }
            var expired = await Read(expiredKey);
            Require(expired is { Expired: true } && expired.ExpiresAt - expired.CreatedAt == TimeSpan.FromHours(24),
                "Expired receipt disappeared instead of retaining its stable expiry boundary.");
            var reassigned = await unit.ExecuteAsync(user, async () => IdentityOperation<bool>.Success(
                await store.TrySaveAsync(user, expiredKey, new string('b', 64), new(2, 2, true), ct)), ct);
            Require(reassigned is { Succeeded: true, Value: false } && await Read(expiredKey) == expired,
                "Expired original key was silently reassigned before maintenance.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("DELETE FROM identity_handle_claim_replays WHERE user_id=@user AND key_id=ANY(@keys);", admin);
            cleanup.Parameters.AddWithValue("user", user); cleanup.Parameters.AddWithValue("keys", new[] { key, expiredKey, rollbackKey });
            await cleanup.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine("Restricted handle claim receipts: owning-subject isolation, original immutable body-free revisions, exact expiry, refusal rollback and retained expired keys passed.");
    }
}
