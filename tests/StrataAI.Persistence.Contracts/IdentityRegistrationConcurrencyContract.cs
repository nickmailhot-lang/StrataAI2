using System.Security.Cryptography;
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

// AUTH-FR-001/002/005; PRD-02-TC-07/08. Actual restricted transactions,
// production verification-mail publication and adaptive hashes. Public signup
// is explicitly enabled for this fixture; no HTTP or mail transport is claimed.
internal static class IdentityRegistrationConcurrencyContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        var ring = JsonSerializer.Serialize(new Dictionary<string, string> {
            ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_ALLOW_SELF_REGISTRATION"] = "true",
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract", ["STRATAAI_AUTH_RETRY_KEYS"] = ring,
            ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = "true", ["STRATAAI_IDENTITY_EMAIL_FROM"] = "contract@example.test",
            ["STRATAAI_PUBLIC_ORIGIN"] = "https://contract.example.test", ["STRATAAI_IDENTITY_EMAIL_ACCOUNT"] = "contract",
            ["STRATAAI_IDENTITY_TOKEN_CURRENT_KEY"] = "contract", ["STRATAAI_IDENTITY_TOKEN_KEYS"] = ring,
        }).Build();
        foreach (var sharedIntent in new[] { false, true })
        {
            var email = $"registration-race-{Guid.NewGuid():N}@example.test";
            const string password = "registration-race-correct-horse";
            var sharedKey = Guid.NewGuid();
            var providers = new List<ServiceProvider>();
            var services = new List<IIdentityService>();
            try {
                for (var lane = 0; lane < 8; lane++) {
                    var context = new RegistrationContext(sharedIntent ? sharedKey : Guid.NewGuid());
                    var collection = new ServiceCollection(); collection.AddLogging();
                    collection.AddSingleton(new PostgresConnectionFactory(apiConnection));
                    collection.AddSingleton<IClock, SystemClock>(); collection.AddSingleton<IIdentityCommandContext>(context);
                    collection.AddSingleton<ICommandActorAuthorization, NoActorFixture>(); collection.AddSingleton<PostgresBackgroundJobStore>();
                    var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
                    collection.AddStrataAiOrganizations(runtime); collection.AddStrataAiWorkManagement(runtime);
                    collection.AddStrataAiIdentity(configuration, runtime);
                    var provider = collection.BuildServiceProvider(); providers.Add(provider);
                    services.Add(provider.GetRequiredService<IIdentityService>());
                }
                var arrived = 0; var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task<IdentityOperation<RegistrationOutcome>> Register(int lane) {
                    if (Interlocked.Increment(ref arrived) == 8) barrier.SetResult();
                    await barrier.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                    var address = (lane % 4) switch {
                        0 => email, 1 => email.ToUpperInvariant(),
                        2 => email[..email.IndexOf('@')].ToUpperInvariant() + "@example.test",
                        _ => "  " + email.Replace("example.test", "EXAMPLE.TEST", StringComparison.Ordinal) + "  ",
                    };
                    return await services[lane].RegisterAsync(address, password, "Registration race", "en-CA", "America/Vancouver", "registration-contract", ct);
                }
                var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(Register)).WaitAsync(TimeSpan.FromSeconds(30), ct);
                Require(outcomes.Count(result => result.Succeeded) == (sharedIntent ? 8 : 1), "Concurrent registration did not preserve intent ownership.");
                Require(outcomes.Where(result => !result.Succeeded).All(result => result.ErrorCode == "email_unavailable" && result.Value is null),
                    "Losing registration intent disclosed an account or used an unstable refusal.");
                var winner = Array.FindIndex(outcomes, result => result.Succeeded);
                var authoritative = outcomes[winner].Value!;
                Require(outcomes.Where(result => result.Succeeded).All(result => result.Value == authoritative), "Shared registration intent produced inconsistent acknowledgments.");
                Require(authoritative.User.Status == AccountStatus.PendingVerification && !authoritative.User.EmailVerified
                    && authoritative.User.Version == 1 && authoritative.User.Locale == "en-CA" && authoritative.User.Timezone == "America/Vancouver",
                    "Production registration did not preserve verification/profile policy.");
                async Task<string> Snapshot() {
                    await using var query = new NpgsqlCommand("""
                        SELECT jsonb_build_object(
                          'users',(SELECT jsonb_agg(to_jsonb(u) ORDER BY id) FROM users u WHERE email_normalized=upper(@email)),
                          'tokens',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM email_verification_tokens t WHERE user_id=@actor),
                          'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM identity_delivery_jobs j WHERE user_id=@actor),
                          'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id=@actor),
                          'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id=@actor),
                          'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_registration_replays r WHERE user_id=@actor))::text;
                        """, admin);
                    query.Parameters.AddWithValue("email", email); query.Parameters.AddWithValue("actor", authoritative.User.Id);
                    return (string)(await query.ExecuteScalarAsync(ct))!;
                }
                var saved = await Snapshot();
                using (var document = JsonDocument.Parse(saved)) {
                    foreach (var field in new[] { "users", "tokens", "jobs", "audits", "events", "receipts" })
                        Require(document.RootElement.GetProperty(field).GetArrayLength() == 1, "Concurrent registration duplicated canonical publication.");
                    var source = document.RootElement.GetProperty("events")[0];
                    Require(source.GetProperty("event_type").GetString() == "USER_REGISTERED"
                        && source.GetProperty("actor_id").GetGuid() == authoritative.User.Id
                        && source.GetProperty("entity_id").GetGuid() == authoritative.User.Id
                        && source.GetProperty("entity_type").GetString() == "User"
                        && source.GetProperty("entity_version").GetInt64() == 1
                        && !source.GetProperty("metadata").EnumerateObject().Any(),
                        "Canonical registration event attribution or version is incorrect.");
                    Require(document.RootElement.GetProperty("users")[0].GetProperty("id").GetGuid() == authoritative.User.Id
                        && document.RootElement.GetProperty("receipts")[0].GetProperty("key_id").GetGuid()
                            == providers[winner].GetRequiredService<IIdentityCommandContext>().IdempotencyKey
                        && document.RootElement.GetProperty("jobs")[0].GetProperty("purpose").GetString() == "VERIFY_EMAIL",
                        "Registration receipt or verification publication belongs to another intent.");
                    var hash = document.RootElement.GetProperty("users")[0].GetProperty("password_hash").GetString()!;
                    var verification = providers[winner].GetRequiredService<IPasswordHashService>().Verify(authoritative.User.Id, hash, password);
                    Require(verification.IsValid && !verification.NeedsRehash && !saved.Contains(password, StringComparison.Ordinal)
                        && authoritative.VerificationToken is not null && !saved.Contains(authoritative.VerificationToken, StringComparison.Ordinal),
                        "Registration stored a raw credential or did not use the configured adaptive hash.");
                }
                var replay = await services[winner].RegisterAsync(email.ToUpperInvariant(), password, "Registration race", "en-CA", "America/Vancouver", "registration-contract", ct);
                Require(replay.Succeeded && replay.Value == authoritative && saved == await Snapshot(), "Registration retry changed canonical state.");
                var wrongPassword = await services[winner].RegisterAsync(email, "wrong-registration-correct-horse", "Registration race", "en-CA", "America/Vancouver", "registration-contract", ct);
                Require(!wrongPassword.Succeeded && wrongPassword.Value is null && wrongPassword.ErrorCode == "email_unavailable" && saved == await Snapshot(),
                    "Unproven registration replay disclosed or changed canonical state.");
                var changedIntent = await services[winner].RegisterAsync(email, password, "Changed registration intent", "en-CA", "America/Vancouver", "registration-contract", ct);
                Require(!changedIntent.Succeeded && changedIntent.Value is null && changedIntent.ErrorCode == "idempotency_key_reused" && saved == await Snapshot(),
                    "Changed registration intent reused a receipt or altered canonical state.");
            }
            finally { foreach (var provider in providers) await provider.DisposeAsync(); }
        }
        Console.WriteLine("Restricted PostgreSQL registration: distinct and shared intent eight-client case-insensitive races passed.");
    }
    private sealed class RegistrationContext(Guid key) : IIdentityCommandContext
    {
        public Guid? IdempotencyKey => key;
        public string? RevocationSessionTokenHash => null;
    }
}
