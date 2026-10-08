using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// FOUND-FR-009, AUTH-FR-004/006/008, PRD-02-TC-07: actual restricted writers.
// Direct store/transaction calls exercise persistence, not HTTP or mail delivery.
internal static class IdentityLifecycleClockContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        static void Require(bool value) { if (!value) throw new InvalidOperationException("Identity lifecycle clock contract failed."); }
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICommandActorAuthorization, NoActorFixture>();
        services.AddSingleton<PostgresBackgroundJobStore>();
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        var ring = JsonSerializer.Serialize(new Dictionary<string,string> {
            ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        services.AddStrataAiIdentity(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract", ["STRATAAI_AUTH_RETRY_KEYS"] = ring,
        }).Build(), runtime);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IIdentityStore>();
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var tokens = provider.GetRequiredService<ISecureTokenService>();
        var hashes = provider.GetRequiredService<IPasswordHashService>();
        var actor = Guid.NewGuid(); var email = $"identity-clocks-{actor:N}@example.test";
        var created = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero).AddMinutes(-1);
        var verified = created.AddSeconds(10); var revoked = created.AddSeconds(20);
        var resetAt = created.AddSeconds(30); var deactivated = created.AddSeconds(40);
        var verification = new SecurityTokenRecord(Guid.NewGuid(), actor, tokens.Hash(tokens.Generate()), created, created.AddHours(1));
        var reset = new SecurityTokenRecord(Guid.NewGuid(), actor, tokens.Hash(tokens.Generate()), created, created.AddHours(1));
        var first = new SessionRecord(Guid.NewGuid(), actor, tokens.Hash(tokens.Generate()), created, created.AddHours(1));
        var second = new SessionRecord(Guid.NewGuid(), actor, tokens.Hash(tokens.Generate()), created, created.AddHours(1));
        var third = new SessionRecord(Guid.NewGuid(), actor, tokens.Hash(tokens.Generate()), resetAt, created.AddHours(1));
        Require(await store.TryCreateUserAsync(new(actor,email,email.ToUpperInvariant(),"Clock fixture",null,"en-CA","UTC",
            AccountStatus.Active,false,hashes.Hash(actor,"identity-clock-correct-horse"),created,created,1),verification,null,ct));
        await store.CreatePasswordResetTokenAsync(reset,null,ct);
        await store.CreateSessionAsync(first,ct); await store.CreateSessionAsync(second,ct);
        async Task Clock(string table, Guid id, DateTimeOffset expectedCreated, DateTimeOffset expectedUpdated)
        {
            // Table names are fixed contract call sites; no external SQL identifier.
            Require(table is "sessions" or "password_reset_tokens" or "email_verification_tokens");
            await using var query = new NpgsqlCommand($"SELECT created_at=@created AND updated_at=@updated FROM {table} WHERE id=@id",admin);
            query.Parameters.AddWithValue("id",id); query.Parameters.AddWithValue("created",expectedCreated);
            query.Parameters.AddWithValue("updated",expectedUpdated); Require(await query.ExecuteScalarAsync(ct) is true);
        }
        async Task<string> Snapshot()
        {
            await using var query = new NpgsqlCommand("""
                SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id=@actor),
                  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id=@actor),
                  'reset',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM password_reset_tokens t WHERE user_id=@actor),
                  'verification',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM email_verification_tokens t WHERE user_id=@actor))::text;
                """,admin);
            query.Parameters.AddWithValue("actor",actor); return (string)(await query.ExecuteScalarAsync(ct))!;
        }
        await Clock("sessions",first.Id,created,created); await Clock("sessions",second.Id,created,created);
        await Clock("password_reset_tokens",reset.Id,created,created);
        await Clock("email_verification_tokens",verification.Id,created,created);
        // A refused owning transaction must roll back the new clock along with
        // consumption, password replacement and revocation of both sessions.
        var before = await Snapshot();
        var refused = await unit.ExecuteTokenProofAsync(async () => {
            Require(await store.CompletePasswordResetAsync(reset.TokenHash,hashes.Hash(actor,"clock-rolled-back-password"),resetAt,ct));
            return IdentityOperation<UserProfile>.Failure("fixture_refusal");
        },ct);
        Require(!refused.Succeeded && before == await Snapshot());
        Require(await store.VerifyEmailAsync(verification.TokenHash,verified,ct));
        await Clock("email_verification_tokens",verification.Id,created,verified);
        before = await Snapshot(); Require(!await store.VerifyEmailAsync(verification.TokenHash,verified.AddSeconds(1),ct));
        Require(before == await Snapshot());
        await store.RevokeSessionAsync(first.TokenHash,revoked,ct);
        await Clock("sessions",first.Id,created,revoked);
        before = await Snapshot(); await store.RevokeSessionAsync(first.TokenHash,revoked.AddSeconds(1),ct);
        Require(before == await Snapshot());
        Require(await store.CompletePasswordResetAsync(reset.TokenHash,hashes.Hash(actor,"clock-accepted-password"),resetAt,ct));
        await Clock("password_reset_tokens",reset.Id,created,resetAt);
        await Clock("sessions",first.Id,created,revoked); await Clock("sessions",second.Id,created,resetAt);
        before = await Snapshot(); Require(!await store.CompletePasswordResetAsync(reset.TokenHash,"unused-refused-hash",resetAt.AddSeconds(1),ct));
        Require(before == await Snapshot());
        await store.CreateSessionAsync(third,ct); await Clock("sessions",third.Id,resetAt,resetAt);
        Require(await store.DeactivateUserAsync(actor,deactivated,ct));
        await Clock("sessions",third.Id,resetAt,deactivated);
        await Clock("sessions",first.Id,created,revoked); await Clock("sessions",second.Id,created,resetAt);
        before = await Snapshot(); Require(!await store.DeactivateUserAsync(actor,deactivated.AddSeconds(1),ct));
        Require(before == await Snapshot());
        Console.WriteLine("Restricted identity lifecycle clocks: creation, verification, reset, revocation, deactivation, repeated refusal and owning rollback passed.");
    }
}
