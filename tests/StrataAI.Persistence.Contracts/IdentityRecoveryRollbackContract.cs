using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// PRD-02-TC-05/06/07/09: real restricted PostgreSQL recovery transactions.
// Accounts and failure timing are fixtures; no HTTP admission or mail transport
// is claimed. Production token, audit, outbox and receipt adapters are real.
internal static class IdentityRecoveryRollbackContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        var context = new RecoveryContext();
        var keyRing = JsonSerializer.Serialize(new Dictionary<string, string> {
            ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract",
            ["STRATAAI_AUTH_RETRY_KEYS"] = keyRing,
            ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = "true",
            ["STRATAAI_IDENTITY_EMAIL_FROM"] = "contract@example.test",
            ["STRATAAI_PUBLIC_ORIGIN"] = "https://contract.example.test",
            ["STRATAAI_IDENTITY_EMAIL_ACCOUNT"] = "contract",
            ["STRATAAI_IDENTITY_TOKEN_CURRENT_KEY"] = "contract",
            ["STRATAAI_IDENTITY_TOKEN_KEYS"] = keyRing,
        }).Build();
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var services = new ServiceCollection(); services.AddLogging();
        var applicationName = $"recovery-contract-{Guid.NewGuid():N}";
        var connection = new NpgsqlConnectionStringBuilder(apiConnection) { ApplicationName = applicationName };
        services.AddSingleton(new PostgresConnectionFactory(connection.ConnectionString));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdentityCommandContext>(context);
        services.AddSingleton<ICommandActorAuthorization, NoActorFixture>();
        services.AddSingleton<PostgresBackgroundJobStore>();
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        services.AddStrataAiIdentity(configuration, runtime);
        await using var provider = services.BuildServiceProvider();
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var identity = provider.GetRequiredService<IdentityService>();
        var receipts = provider.GetRequiredService<IIdentityRecoveryRequestReplayStore>();
        var tokens = provider.GetRequiredService<ISecureTokenService>();
        var store = provider.GetRequiredService<IIdentityStore>();
        foreach (var verification in new[] { false, true })
        foreach (var failure in new[] { "database", "cancel", "cancel_and_database" })
        {
            var actor = Guid.NewGuid(); var email = $"recovery-contract-{actor:N}@example.test";
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
                VALUES(@actor,@email,upper(@email),'Recovery contract',@status,@verified,'unused-contract-hash',now(),now());
                """, admin))
            {
                seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", email);
                seed.Parameters.AddWithValue("status", verification ? "PENDING_VERIFICATION" : "ACTIVE");
                seed.Parameters.AddWithValue("verified", !verification); await seed.ExecuteNonQueryAsync(ct);
            }
            var purpose = verification ? IdentityTokenPurpose.VerifyEmail : IdentityTokenPurpose.ResetPassword;
            Task<string?> Request(CancellationToken token) => verification
                ? identity.RequestEmailVerificationAsync(email, "recovery-contract", token)
                : Reset(token);
            async Task<string?> Reset(CancellationToken token) =>
                (await identity.RequestPasswordResetAsync(email, "recovery-contract", token)).ResetToken;
            async Task<string> Snapshot()
            {
                await using var query = new NpgsqlCommand("""
                    SELECT jsonb_build_object(
                      'user',(SELECT to_jsonb(u) FROM users u WHERE id=@actor),
                      'reset',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM password_reset_tokens t WHERE user_id=@actor),
                      'verification',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM email_verification_tokens t WHERE user_id=@actor),
                      'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id,operation) FROM identity_recovery_request_replays r WHERE user_id=@actor),
                      'consumption',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id,operation) FROM identity_token_consumption_replays r WHERE user_id=@actor),
                      'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM identity_delivery_jobs j WHERE user_id=@actor),
                      'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id=@actor),
                      'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id=@actor),
                      'stream',(SELECT to_jsonb(s) FROM identity_event_streams s WHERE user_id=@actor),
                      'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id=@actor))::text;
                    """, admin);
                query.Parameters.AddWithValue("actor", actor);
                return (string)(await query.ExecuteScalarAsync(ct))!;
            }
            // Keep an actual earlier token, mail job, audit and retry receipt.
            context.IdempotencyKey = Guid.NewGuid();
            var original = await unit.ExecuteRecoveryRequestAsync(() => Request(ct), null, ct);
            Require(original is not null, "Original recovery publication failed.");
            var before = await Snapshot();
            context.IdempotencyKey = Guid.NewGuid();
            var key = context.IdempotencyKey.Value;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            string? failedToken = null;
            var changing = unit.ExecuteRecoveryRequestAsync<string?>(async () => {
                failedToken = await Request(cancellation.Token);
                Require(failedToken is not null, "Recovery failed before publication.");
                var receipt = await receipts.ReadAsync(actor, key, purpose, ct);
                Require(receipt is { TokenSource: RecoveryTokenSource.EmailDelivery }, "Real recovery receipt was not published.");
                Require((await store.FindSecurityTokenRetryProofAsync(tokens.Hash(failedToken!), purpose, DateTimeOffset.UtcNow, ct))?.User.Id == actor,
                    "Real recovery proof was not published.");
                if (failure != "database") cancellation.Cancel();
                if (failure != "cancel") {
                    // Duplicate the actual primary key through the restricted
                    // adapter with the uncancelled test token. This guarantees
                    // a real server constraint error after all request writes,
                    // even when cancellation is already pending.
                    await receipts.SaveAsync(actor, key, purpose, receipt!, ct);
                }
                return failedToken;
            }, null, cancellation.Token);
            try {
                var result = await changing;
                Require(failure == "database" && result is null, "Recovery cancellation was masked or database failure was disclosed.");
            }
            catch (OperationCanceledException) when (failure != "database") { }
            Require(failedToken is not null && before == await Snapshot(), "Recovery failure changed prior persisted state.");
            var retry = await unit.ExecuteRecoveryRequestAsync(() => Request(ct), null, ct);
            Require(retry is not null && retry != failedToken, "Same-key recovery did not publish a fresh proof after rollback.");
            var saved = await Snapshot();
            using (var persisted = JsonDocument.Parse(saved)) {
                foreach (var field in new[] { verification ? "verification" : "reset", "receipts", "jobs", "audits" })
                    Require(persisted.RootElement.GetProperty(field).GetArrayLength() == 2,
                        "Original and recovered publications were not exactly one each.");
            }
            var replay = await unit.ExecuteRecoveryRequestAsync(() => Request(ct), null, ct);
            Require(replay == retry && saved == await Snapshot(), "Recovery replay duplicated or changed persisted state.");
            await unit.ExecuteRecoveryRequestAsync(async () => {
                Require(await store.FindSecurityTokenRetryProofAsync(tokens.Hash(failedToken!), purpose, DateTimeOffset.UtcNow, ct) is null,
                    "Failed recovery proof survived rollback.");
                Require((await store.FindSecurityTokenRetryProofAsync(tokens.Hash(original!), purpose, DateTimeOffset.UtcNow, ct))?.User.Id == actor,
                    "Original recovery proof was lost.");
                Require((await store.FindSecurityTokenRetryProofAsync(tokens.Hash(retry!), purpose, DateTimeOffset.UtcNow, ct))?.User.Id == actor,
                    "Recovered proof is unavailable.");
                return true;
            }, false, ct);
            if (failure == "database")
                await VerifyElapsedConsumptionAsync(admin, unit, identity, context, actor, purpose, retry!, applicationName, Snapshot, ct);
        }
        Console.WriteLine("Restricted PostgreSQL recovery rollback: six failure/cancellation and two elapsed consumption-expiry cases passed.");
    }

    private static async Task VerifyElapsedConsumptionAsync(NpgsqlConnection admin, IIdentityUnitOfWork unit,
        IdentityService identity, RecoveryContext context, Guid actor, IdentityTokenPurpose purpose,
        string token, string applicationName, Func<Task<string>> snapshot, CancellationToken ct)
    {
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        var table = purpose == IdentityTokenPurpose.VerifyEmail ? "email_verification_tokens" : "password_reset_tokens";
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var faultName = $"ci_recovery_expiry_{actor:N}";
        async Task Sql(string sql) {
            await using var command = new NpgsqlCommand(sql, admin);
            await command.ExecuteNonQueryAsync(ct);
        }
        context.IdempotencyKey = Guid.NewGuid();
        Task<IdentityOperation<UserProfile>> Consume() => unit.ExecuteTokenProofAsync(() =>
            purpose == IdentityTokenPurpose.VerifyEmail
                ? identity.VerifyEmailAsync(token, "recovery-contract", ct)
                : identity.ResetPasswordAsync(token, "replacement-contract-correct-horse", "recovery-contract", ct), ct);
        // Identifiers and actor literals derive exclusively from this fixture's
        // generated UUID; no application input is interpolated into admin SQL.
        await Sql($$"""
            CREATE FUNCTION public.{{faultName}}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN PERFORM pg_sleep(12); RETURN NEW; END $body$;
            CREATE TRIGGER {{faultName}} AFTER INSERT ON identity_token_consumption_replays
              FOR EACH ROW WHEN (NEW.user_id='{{actor:D}}'::uuid) EXECUTE FUNCTION public.{{faultName}}();
            UPDATE {{table}} SET expires_at=clock_timestamp()+interval '8 seconds'
              WHERE user_id='{{actor:D}}'::uuid AND token_hash='{{tokenHash}}' AND used_at IS NULL;
            """);
        try {
            var before = await snapshot();
            var consuming = Consume();
            var observedPublication = false;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!consuming.IsCompleted && DateTimeOffset.UtcNow < deadline) {
                await using var probe = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename='strataai_api_runtime'
                      AND application_name=@application
                      AND wait_event='PgSleep' AND query LIKE '%INSERT INTO identity_token_consumption_replays%');
                    """, admin);
                probe.Parameters.AddWithValue("application", applicationName);
                if (await probe.ExecuteScalarAsync(ct) is true) { observedPublication = true; break; }
                await Task.Delay(100, ct);
            }
            var refused = await consuming.WaitAsync(TimeSpan.FromSeconds(25), ct);
            Require(observedPublication, "Elapsed-expiry case never reached actual consumption receipt publication.");
            Require(!refused.Succeeded && refused.ErrorCode == "invalid_or_expired_token" && refused.Value is null,
                "Elapsed token expiry disclosed a profile or acknowledged consumption.");
            Require(before == await snapshot(), "Elapsed token expiry failed to restore complete persisted state.");
        }
        finally {
            await Sql($"DROP TRIGGER IF EXISTS {faultName} ON identity_token_consumption_replays; DROP FUNCTION IF EXISTS public.{faultName}();");
        }
        // Restore only this disposable token's expiry to exercise the existing
        // same-key success/replay and single-use semantics after rollback.
        await Sql($"UPDATE {table} SET expires_at=clock_timestamp()+interval '30 minutes' WHERE user_id='{actor:D}'::uuid AND token_hash='{tokenHash}' AND used_at IS NULL;");
        var recovered = await Consume();
        Require(recovered.Succeeded && recovered.Value is not null, "Same-key token consumption did not recover after rollback.");
        var saved = await snapshot();
        var replay = await Consume();
        Require(replay.Succeeded && replay.Value == recovered.Value && saved == await snapshot(),
            "Consumption acknowledgment replay changed canonical state.");
        context.IdempotencyKey = Guid.NewGuid();
        var duplicate = await Consume();
        Require(!duplicate.Succeeded && duplicate.ErrorCode == "invalid_or_expired_token" && duplicate.Value is null && saved == await snapshot(),
            "A new intent consumed the same token twice.");
    }
    private sealed class RecoveryContext : IIdentityCommandContext
    {
        public Guid? IdempotencyKey { get; set; }
        public string? RevocationSessionTokenHash => null;
    }
}
