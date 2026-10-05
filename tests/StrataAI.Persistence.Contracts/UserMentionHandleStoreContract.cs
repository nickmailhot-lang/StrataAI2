using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Actual PostgreSQL/restricted API/owning identity unit. Actor eligibility is a
// synthetic fixture; this does not claim HTTP session or Board disclosure proof.
internal static class UserMentionHandleStoreContract
{
    private sealed class ActorFixture : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var first = users[0]; var at = DateTimeOffset.UtcNow;
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
            SELECT id,'handle-'||id::text||'@example.test',upper('handle-'||id::text||'@example.test'),
              'Same display name','ACTIVE',true,'fixture',@at,@at FROM unnest(@users) id;
            """, admin))
        {
            seed.Parameters.AddWithValue("users", users); seed.Parameters.AddWithValue("at", at);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IClock, SystemClock>(); services.AddSingleton<ICommandActorAuthorization, ActorFixture>();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<PostgresBackgroundJobStore>();
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract",
            ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(new Dictionary<string, string>
            { ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })
        }).Build();
        services.AddStrataAiIdentity(settings, runtime); services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        await SearchInteractionSourceContract.RunAsync(admin, provider, first, users[1], ct);
        var store = provider.GetRequiredService<IUserMentionHandleStore>(); var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        async Task<UserMentionHandle> Current(Guid user)
        {
            var result = await unit.ExecuteAsync(user, async () => IdentityOperation<UserMentionHandle>.Success(
                await store.FindAsync(user, ct) ?? throw new InvalidOperationException("Handle fixture is unavailable.")), ct);
            Require(result.Succeeded, "Owning handle read failed."); return result.Value!;
        }
        Task<IdentityOperation<UserMentionHandleChange>> Claim(Guid user, string handle, long version, DateTimeOffset? timestamp = null)
            => unit.ExecuteAsync(user, () => store.ClaimAsync(user, handle, version, timestamp ?? at.AddMinutes(1), ct), ct);
        async Task<long> ReservationCount(Guid user)
        {
            await using var query = new NpgsqlCommand("SELECT count(*) FROM mention_handle_reservations WHERE user_id=@user;", admin);
            query.Parameters.AddWithValue("user", user); return (long)(await query.ExecuteScalarAsync(ct))!;
        }
        try
        {
            try { await store.FindAsync(first, ct); throw new InvalidOperationException("Unowned handle read was accepted."); }
            catch (InvalidOperationException error) when (error.Message == "Mention handles require an owning identity transaction.") { }
            try { await store.ClaimAsync(first, "unowned", 1, at, ct); throw new InvalidOperationException("Unowned handle claim was accepted."); }
            catch (InvalidOperationException error) when (error.Message == "Mention handles require an owning identity transaction.") { }
            var initial = await Current(first);
            var isolated = await unit.ExecuteAsync<bool>(first, async () =>
            {
                try { await store.FindAsync(users[1], ct); throw new InvalidOperationException("Foreign subject handle read was accepted."); }
                catch (InvalidOperationException error) when (error.Message == "Mention handles require an owning identity transaction.") { }
                try { await store.ClaimAsync(users[1], "foreign_scope", 1, at.AddMinutes(1), ct); throw new InvalidOperationException("Foreign subject handle claim was accepted."); }
                catch (InvalidOperationException error) when (error.Message == "Mention handles require an owning identity transaction.") { }
                return IdentityOperation<bool>.Success(true);
            }, ct);
            Require(isolated.Succeeded && (await Current(users[1])).Version == 1,
                "Owning identity subject was not isolated or foreign claim advanced identity.");
            Require(initial.UserId == first && initial.Handle == $"u_{first:N}" && initial.Version == 1
                && initial.CreatedAt == initial.UpdatedAt, "Adapter lost seeded account identity/history.");
            var claimed = "handle_" + Guid.NewGuid().ToString("N")[..16];
            var changed = await Claim(first, "  " + claimed.ToUpperInvariant() + "  ", 1);
            Require(changed is { Succeeded: true, Value.Changed: true, Value.Current.Version: 2 }
                && changed.Value.Current.Handle == claimed && changed.Value.Current.CreatedAt == initial.CreatedAt,
                "Adapter lost normalization or exact one-revision claim history.");
            var current = changed.Value!.Current;
            var noop = await Claim(first, claimed, 2, initial.CreatedAt.AddSeconds(-1));
            Require(noop is { Succeeded: true, Value.Changed: false } && noop.Value.Current == current,
                "Equal handle claim changed history or required a fabricated new timestamp.");
            Require((await Claim(first, claimed, 1)).ErrorCode == "version_conflict"
                && (await Claim(first, "valid_past", 2, initial.CreatedAt.AddSeconds(-1))).ErrorCode == "version_conflict",
                "Stale revision/past timestamp was admitted.");
            foreach (var invalid in new[] { "card", "board", "@member", "nïck", "u_foreign", $"u_{users[1]:N}", new string('x', 41) })
                Require((await Claim(first, invalid, 2)).ErrorCode == "mention_handle_invalid", "Invalid/reserved handle reached storage.");
            Require(await Current(first) == current && await ReservationCount(first) == 2,
                "Refused/no-op claims changed registry history.");
            var undo = "undo_" + Guid.NewGuid().ToString("N");
            var refused = await unit.ExecuteAsync<bool>(first, async () =>
            {
                Require((await store.ClaimAsync(first, undo, 2, at.AddMinutes(1), ct)).Succeeded, "Rollback setup claim failed.");
                return IdentityOperation<bool>.Failure("fixture_refused");
            }, ct);
            Require(refused.ErrorCode == "fixture_refused" && await Current(first) == current && await ReservationCount(first) == 2,
                "A handle/reservation survived owning transaction refusal.");
            var race = "race_" + Guid.NewGuid().ToString("N");
            var racing = await Task.WhenAll(Claim(users[1], race, 1), Claim(users[2], race, 1));
            Require(racing.Count(x => x.Succeeded) == 1 && racing.Count(x => x.ErrorCode == "mention_handle_unavailable") == 1,
                "Concurrent accounts did not produce exactly one authoritative handle owner.");
            var winner = racing[0].Succeeded ? users[1] : users[2]; var loser = racing[0].Succeeded ? users[2] : users[1];
            Require((await Current(loser)).Version == 1 && await ReservationCount(loser) == 1,
                "Losing collision advanced current identity or leaked a reservation.");
            var recovered = await unit.ExecuteAsync<bool>(first, async () =>
            {
                Require((await store.ClaimAsync(first, race, 2, at.AddMinutes(1), ct)).ErrorCode == "mention_handle_unavailable",
                    "Known owner collision was not a stable refusal.");
                Require(await store.FindAsync(first, ct) == current, "Collision poisoned the owning transaction read.");
                return IdentityOperation<bool>.Success(true);
            }, ct);
            Require(recovered.Succeeded && await Current(first) == current, "Collision savepoint did not preserve the owning transaction.");
            var other = "next_" + Guid.NewGuid().ToString("N");
            Require((await Claim(winner, other, 2)).Succeeded && (await Claim(loser, race, 1)).ErrorCode == "mention_handle_unavailable",
                "Renaming released a former handle to a different account.");
            Require((await Claim(winner, race, 3)).Succeeded && await ReservationCount(winner) == 3,
                "Same-owner former-handle reclaim was lost or allocated a duplicate slot.");
            Require((await Claim(first, initial.Handle, 2)).Succeeded && (await Current(first)).Version == 3,
                "Account could not return to its own reserved generated default.");
            for (var index = 0; index < 30; index++)
            {
                var fresh = "b_" + first.ToString("N")[..16] + "_" + index;
                Require((await Claim(first, fresh, 3 + index)).Succeeded, "Valid lifetime reservation unexpectedly failed.");
            }
            var capped = await Current(first);
            Require(capped.Version == 33 && await ReservationCount(first) == 32, "Adapter reservation quota fixture is incorrect.");
            var bounded = await unit.ExecuteAsync<bool>(first, async () =>
            {
                Require((await store.ClaimAsync(first, "overflow_" + first.ToString("N")[..16], 33, at.AddMinutes(1), ct)).ErrorCode == "mention_handle_claim_refused",
                    "Reservation overflow was not a stable policy refusal.");
                Require(await store.FindAsync(first, ct) == capped, "Policy refusal poisoned the owning transaction.");
                return IdentityOperation<bool>.Success(true);
            }, ct);
            Require(bounded.Succeeded && await ReservationCount(first) == 32 && (await Claim(first, claimed, 33)).Succeeded,
                "Policy savepoint leaked aliases or blocked reclaim at the quota.");
            await HandleClaimReplayContract.RunAsync(admin, provider, first, users[1], ct);
            await UserMentionHandleCommandContract.RunAsync(admin, provider, ct);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("DELETE FROM users WHERE id=ANY(@users);", admin);
            cleanup.Parameters.AddWithValue("users", users); await cleanup.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine("Restricted mention-handle adapter: owning subject/read-write isolation, normalization, CAS/no-op, rollback, concurrent collision, former/default reclaim, reservation bound and recovered savepoints passed.");
    }
}
