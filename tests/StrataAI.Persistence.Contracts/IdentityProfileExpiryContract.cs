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

// PRD-02-TC-05/07: elapsed session expiry after real profile publication.
// The context models an admitted request; this is not an HTTP-host fixture.
internal static class IdentityProfileExpiryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        foreach (var keyed in new[] { false, true })
        {
            var actor = Guid.NewGuid(); var sessionId = Guid.NewGuid();
            var hash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var context = new ProfileContext(actor, hash) { IdempotencyKey = keyed ? Guid.NewGuid() : null };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract",
                ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(new Dictionary<string, string> {
                    ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }),
            }).Build();
            var applicationName = $"profile-expiry-contract-{actor:N}";
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton(new PostgresConnectionFactory(new NpgsqlConnectionStringBuilder(apiConnection) {
                ApplicationName = applicationName }.ConnectionString));
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<ICommandActorContext>(context); services.AddSingleton<IIdentityCommandContext>(context);
            services.AddSingleton<ICommandActorAuthorization, CommandActorAuthorization>();
            services.AddSingleton<PostgresBackgroundJobStore>();
            var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
            services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
            services.AddStrataAiIdentity(configuration, runtime);
            await using var provider = services.BuildServiceProvider();
            var identity = provider.GetRequiredService<IIdentityService>();
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
                VALUES(@actor,@email,upper(@email),'Original profile','ACTIVE',true,'unused-contract-hash',now(),now());
                INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at)
                VALUES(@session,@actor,@hash,clock_timestamp(),clock_timestamp()+interval '30 minutes');
                """, admin))
            {
                seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"profile-expiry-{actor:N}@example.test");
                seed.Parameters.AddWithValue("session", sessionId); seed.Parameters.AddWithValue("hash", hash);
                await seed.ExecuteNonQueryAsync(ct);
            }
            async Task<string> Snapshot()
            {
                await using var query = new NpgsqlCommand("""
                    SELECT jsonb_build_object(
                      'user',(SELECT to_jsonb(u) FROM users u WHERE id=@actor),
                      'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id=@actor),
                      'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_profile_replays r WHERE user_id=@actor),
                      'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id=@actor),
                      'stream',(SELECT to_jsonb(s) FROM identity_event_streams s WHERE user_id=@actor),
                      'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id=@actor))::text;
                    """, admin);
                query.Parameters.AddWithValue("actor", actor); return (string)(await query.ExecuteScalarAsync(ct))!;
            }
            async Task Sql(string text)
            { await using var command = new NpgsqlCommand(text, admin); await command.ExecuteNonQueryAsync(ct); }
            Task<IdentityOperation<UserProfile>> Update(string name = "Elapsed profile") => identity.UpdateProfileAsync(
                actor, name, "https://example.test/avatar.png", "en-CA", "America/Vancouver", 1, "profile-expiry-contract", ct);
            var table = keyed ? "identity_profile_replays" : "identity_events";
            var fault = $"ci_profile_expiry_{actor:N}";
            // Fixture-generated identifiers only. The trigger verifies the
            // actual profile/audit/event writes before admitting the sleep.
            await Sql($$"""
                CREATE FUNCTION public.{{fault}}() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
                  SET search_path=pg_catalog,public AS $body$
                BEGIN
                  IF NOT EXISTS(SELECT 1 FROM users WHERE id='{{actor:D}}'::uuid AND version=2 AND display_name='Elapsed profile')
                    OR NOT EXISTS(SELECT 1 FROM audit_events WHERE actor_id='{{actor:D}}'::uuid AND event_type='USER_PROFILE_UPDATED')
                    OR NOT EXISTS(SELECT 1 FROM identity_events WHERE user_id='{{actor:D}}'::uuid AND event_type='USER_PROFILE_UPDATED' AND entity_version=2)
                  THEN RAISE EXCEPTION 'Profile publication was not reached'; END IF;
                  PERFORM pg_sleep(12); RETURN NEW;
                END $body$;
                CREATE TRIGGER {{fault}} AFTER INSERT ON {{table}}
                  FOR EACH ROW WHEN (NEW.user_id='{{actor:D}}'::uuid) EXECUTE FUNCTION public.{{fault}}();
                UPDATE sessions SET expires_at=clock_timestamp()+interval '8 seconds' WHERE id='{{sessionId:D}}'::uuid;
                """);
            try
            {
                var before = await Snapshot(); var updating = Update(); var observed = false;
                var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
                while (!updating.IsCompleted && DateTimeOffset.UtcNow < deadline)
                {
                    await using var probe = new NpgsqlCommand("""
                        SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename='strataai_api_runtime'
                          AND application_name=@application AND wait_event='PgSleep' AND query LIKE @query);
                        """, admin);
                    probe.Parameters.AddWithValue("application", applicationName);
                    probe.Parameters.AddWithValue("query", $"%INSERT INTO {table}%");
                    if (await probe.ExecuteScalarAsync(ct) is true) { observed = true; break; }
                    await Task.Delay(100, ct);
                }
                var refused = await updating.WaitAsync(TimeSpan.FromSeconds(25), ct);
                Require(observed, $"Profile expiry never reached observed persisted publication; result={refused.ErrorCode}.");
                Require(!refused.Succeeded && refused.ErrorCode == "session_unavailable" && refused.Value is null,
                    "Expired profile command disclosed or acknowledged a changed profile.");
                Require(before == await Snapshot(), "Expired profile command failed complete user/session/audit/event/receipt rollback.");
            }
            finally { await Sql($"DROP TRIGGER IF EXISTS {fault} ON {table}; DROP FUNCTION IF EXISTS public.{fault}();"); }
            // Restore only the disposable session's lifetime for ordinary retry.
            await Sql($"UPDATE sessions SET expires_at=clock_timestamp()+interval '30 minutes' WHERE id='{sessionId:D}'::uuid;");
            using var retryBaseline = JsonDocument.Parse(await Snapshot());
            var sessionBaseline = retryBaseline.RootElement.GetProperty("sessions").GetRawText();
            var recovered = await Update();
            Require(recovered.Succeeded && recovered.Value is { Version: 2, DisplayName: "Elapsed profile", Locale: "en-CA", Timezone: "America/Vancouver" },
                "Profile command did not recover with the original intent after expiry rollback.");
            Require(recovered.Value!.Id == actor && recovered.Value.AvatarUrl == "https://example.test/avatar.png"
                && recovered.Value.Status == AccountStatus.Active && recovered.Value.EmailVerified,
                "Recovered profile acknowledgment changed its account or complete fields.");
            var saved = await Snapshot();
            using (var publication = JsonDocument.Parse(saved))
            {
                var root = publication.RootElement;
                var events = root.GetProperty("events"); var receipts = root.GetProperty("receipts");
                Require(events.GetArrayLength() == 1 && root.GetProperty("audits").GetArrayLength() == 1
                    && root.GetProperty("stream").GetProperty("last_sequence").GetInt64() == 1
                    && (receipts.ValueKind == JsonValueKind.Null ? 0 : receipts.GetArrayLength()) == (keyed ? 1 : 0),
                    "Recovered profile published duplicate or missing audit/event/receipt state.");
                var source = events[0];
                Require(source.GetProperty("actor_id").GetGuid() == actor && source.GetProperty("entity_id").GetGuid() == actor
                    && source.GetProperty("event_type").GetString() == "USER_PROFILE_UPDATED"
                    && source.GetProperty("entity_version").GetInt64() == 2 && source.GetProperty("metadata").GetRawText() == "{}"
                    && source.GetProperty("correlation_id").GetString() == "profile-expiry-contract"
                    && root.GetProperty("sessions").GetRawText() == sessionBaseline,
                    "Recovered profile lost canonical attribution or changed its original session.");
            }
            var repeated = await Update();
            Require(keyed ? repeated.Succeeded && repeated.Value == recovered.Value : !repeated.Succeeded && repeated.ErrorCode == "version_conflict" && repeated.Value is null,
                "Profile retry/replay did not preserve its keyed or unkeyed semantics.");
            Require(saved == await Snapshot(), "Repeated profile command changed persisted state.");
            var changed = await Update("Different profile");
            Require(!changed.Succeeded && changed.Value is null && changed.ErrorCode == (keyed ? "idempotency_key_reused" : "version_conflict")
                && saved == await Snapshot(), "Changed profile intent modified or disclosed protected state.");
            Console.WriteLine($"Restricted PostgreSQL profile elapsed-expiry rollback and retry/replay passed: keyed={keyed}.");
        }
    }
    private sealed class ProfileContext(Guid actor, string hash) : ICommandActorContext, IIdentityCommandContext
    {
        public bool HasHttpRequest => true;
        public Guid? AuthenticatedUserId => actor;
        public string? SessionTokenHash => hash;
        public string? RevocationSessionTokenHash => hash;
        public Guid? IdempotencyKey { get; set; }
    }
}
