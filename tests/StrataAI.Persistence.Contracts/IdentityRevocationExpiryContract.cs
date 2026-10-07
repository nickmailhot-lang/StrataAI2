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

// PRD-02-TC-05/07/10: actual restricted revocation/assignment transactions.
// Account, graph and admitted request are fixtures; this does not prove HTTP.
internal static class IdentityRevocationExpiryContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        foreach (var deactivation in new[] { false, true })
        {
            var actor = Guid.NewGuid(); var owner = Guid.NewGuid(); var tenant = Guid.NewGuid();
            var board = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid();
            var session = Guid.NewGuid(); var otherSession = Guid.NewGuid();
            var context = new RevocationContext(actor) { IdempotencyKey = Guid.NewGuid() };
            var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract",
                ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(new Dictionary<string, string> {
                    ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }),
            }).Build();
            var applicationName = $"revocation-expiry-contract-{actor:N}";
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton(new PostgresConnectionFactory(new NpgsqlConnectionStringBuilder(apiConnection) {
                ApplicationName = applicationName }.ConnectionString));
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<ICommandActorContext>(context); services.AddSingleton<IIdentityCommandContext>(context);
            services.AddSingleton<ICommandActorAuthorization, CommandActorAuthorization>();
            services.AddSingleton<PostgresBackgroundJobStore>();
            var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
            services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
            services.AddStrataAiIdentity(settings, runtime);
            await using var provider = services.BuildServiceProvider();
            var identity = provider.GetRequiredService<IIdentityService>();
            var tokens = provider.GetRequiredService<ISecureTokenService>();
            var rawSession = tokens.Generate(); context.SessionTokenHash = tokens.Hash(rawSession);
            var otherHash = tokens.Hash(tokens.Generate());
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
                  SELECT id,id::text||'@example.test',upper(id::text||'@example.test'),'Revocation fixture','ACTIVE',true,'unused-contract-hash',now(),now()
                  FROM unnest(ARRAY[@actor,@owner]::uuid[]) id;
                INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at)
                  VALUES(@session,@actor,@hash,clock_timestamp(),clock_timestamp()+interval '30 minutes'),
                    (@other,@actor,@otherhash,clock_timestamp(),clock_timestamp()+interval '30 minutes');
                INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at) VALUES(@tenant,'Revocation fixture',@owner,now(),now());
                INSERT INTO organization_members(id,tenant_id,user_id,role,status)
                  VALUES(gen_random_uuid(),@tenant,@owner,'OWNER','ACTIVE'),(gen_random_uuid(),@tenant,@actor,'MEMBER','ACTIVE');
                INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Revocation Board',now(),now());
                INSERT INTO board_members(id,tenant_id,board_id,user_id,role,created_at,updated_at)
                  VALUES(gen_random_uuid(),@tenant,@board,@actor,'MEMBER',now(),now());
                INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
                  VALUES(@list,@tenant,@board,'Revocation List','500000000000000000000000000000',now(),now());
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
                  VALUES(@card,@tenant,@board,@list,'Revocation Card','500000000000000000000000000000',now(),now());
                INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) VALUES(@tenant,@board,@card,@actor,@owner);
                """, admin))
            {
                foreach (var (name, value) in new[] { ("actor",actor),("owner",owner),("tenant",tenant),("board",board),("list",list),("card",card),("session",session),("other",otherSession) })
                    seed.Parameters.AddWithValue(name, value);
                seed.Parameters.AddWithValue("hash", context.SessionTokenHash!); seed.Parameters.AddWithValue("otherhash", otherHash);
                await seed.ExecuteNonQueryAsync(ct);
            }
            async Task<string> Snapshot()
            {
                await using var query = new NpgsqlCommand("""
                    SELECT jsonb_build_object(
                      'users',(SELECT jsonb_agg(to_jsonb(u) ORDER BY id) FROM users u WHERE id=ANY(ARRAY[@actor,@owner]::uuid[])),
                      'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id=@actor),
                      'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_revocation_replays r WHERE user_id=@actor),
                      'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id=@actor OR tenant_id=@tenant),
                      'identityStream',(SELECT to_jsonb(s) FROM identity_event_streams s WHERE user_id=@actor),
                      'identityEvents',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id=@actor),
                      'proofs',(SELECT jsonb_agg(to_jsonb(p) ORDER BY entity_version) FROM invitation_issuer_authority_proofs p WHERE actor_id=@actor),
                      'sources',(SELECT jsonb_agg(to_jsonb(s) ORDER BY event_id) FROM invitation_issuer_authority_sources s WHERE actor_id=@actor),
                      'routing',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM invitation_issuer_authority_jobs j WHERE actor_id=@actor),
                      'effects',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id,email_normalized) FROM invitation_issuer_authority_effects e WHERE actor_id=@actor),
                      'organization',(SELECT to_jsonb(o) FROM organizations o WHERE id=@tenant),
                      'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM organization_members m WHERE tenant_id=@tenant),
                      'board',(SELECT to_jsonb(b) FROM boards b WHERE id=@board),
                      'boardMembers',(SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM board_members m WHERE tenant_id=@tenant),
                      'list',(SELECT to_jsonb(l) FROM board_lists l WHERE id=@list),
                      'card',(SELECT to_jsonb(c) FROM cards c WHERE id=@card),
                      'assignments',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM card_members m WHERE card_id=@card),
                      'workStream',(SELECT to_jsonb(s) FROM work_event_streams s WHERE board_id=@board),
                      'workEvents',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM work_events e WHERE board_id=@board),
                      'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id=@tenant),
                      'directoryStream',(SELECT to_jsonb(s) FROM organization_board_event_streams s WHERE tenant_id=@tenant),
                      'directoryEvents',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM organization_board_events e WHERE tenant_id=@tenant))::text;
                    """, admin);
                foreach (var (name,value) in new[] { ("actor",actor),("owner",owner),("tenant",tenant),("board",board),("list",list),("card",card) }) query.Parameters.AddWithValue(name,value);
                return (string)(await query.ExecuteScalarAsync(ct))!;
            }
            async Task Sql(string text) { await using var command = new NpgsqlCommand(text, admin); await command.ExecuteNonQueryAsync(ct); }
            Task<IdentityOperation<bool>> Revoke(bool deactivate) => deactivate ? identity.DeactivateAsync(actor,"revocation-expiry-contract",ct)
                : identity.LogoutAsync(rawSession,actor,"revocation-expiry-contract",ct);
            var fault = $"ci_revocation_expiry_{actor:N}";
            var expectedEvent = deactivation ? "USER_DEACTIVATED" : "SESSION_REVOKED";
            var expectedStatus = deactivation ? "DEACTIVATED" : "ACTIVE";
            var expectedVersion = deactivation ? 2 : 1;
            var assignmentTest = deactivation ? "NOT EXISTS" : "EXISTS";
            await Sql($$"""
                CREATE FUNCTION public.{{fault}}() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $body$
                BEGIN
                  IF NOT EXISTS(SELECT 1 FROM users WHERE id='{{actor:D}}'::uuid AND status='{{expectedStatus}}' AND version={{expectedVersion}})
                    OR NOT EXISTS(SELECT 1 FROM sessions WHERE id='{{session:D}}'::uuid AND revoked_at IS NOT NULL)
                    OR NOT EXISTS(SELECT 1 FROM identity_events WHERE user_id='{{actor:D}}'::uuid AND event_type='{{expectedEvent}}' AND entity_version={{expectedVersion}})
                    OR NOT ({{assignmentTest}}(SELECT 1 FROM card_members WHERE card_id='{{card:D}}'::uuid AND user_id='{{actor:D}}'::uuid))
                  THEN RAISE EXCEPTION 'Revocation publication was not reached'; END IF;
                  PERFORM pg_sleep(12); RETURN NEW;
                END $body$;
                CREATE TRIGGER {{fault}} AFTER INSERT ON identity_revocation_replays FOR EACH ROW
                  WHEN (NEW.user_id='{{actor:D}}'::uuid) EXECUTE FUNCTION public.{{fault}}();
                UPDATE sessions SET expires_at=clock_timestamp()+interval '8 seconds' WHERE id='{{session:D}}'::uuid;
                """);
            try
            {
                var before = await Snapshot(); var revoking = Revoke(deactivation); var observed = false;
                var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
                while (!revoking.IsCompleted && DateTimeOffset.UtcNow < deadline)
                {
                    await using var probe = new NpgsqlCommand("""
                        SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND application_name=@application
                          AND wait_event='PgSleep' AND query LIKE '%INSERT INTO identity_revocation_replays%');
                        """,admin);
                    probe.Parameters.AddWithValue("application",applicationName);
                    if (await probe.ExecuteScalarAsync(ct) is true) { observed = true; break; }
                    await Task.Delay(100,ct);
                }
                var refused = await revoking.WaitAsync(TimeSpan.FromSeconds(25),ct);
                Require(observed,$"Revocation expiry never reached observed publication; result={refused.ErrorCode}.");
                Require(!refused.Succeeded && refused.Value is not true && refused.ErrorCode=="session_unavailable","Elapsed revocation expiry acknowledged success.");
                Require(before==await Snapshot(),"Elapsed revocation expiry failed complete identity/assignment/authority/outbox rollback.");
            }
            finally { await Sql($"DROP TRIGGER IF EXISTS {fault} ON identity_revocation_replays; DROP FUNCTION IF EXISTS public.{fault}();"); }
            await Sql($"UPDATE sessions SET expires_at=clock_timestamp()+interval '30 minutes' WHERE id='{session:D}'::uuid;");
            Require((await Revoke(deactivation)).Succeeded,"Original revocation intent did not recover after rollback.");
            var saved = await Snapshot();
            using (var publication = JsonDocument.Parse(saved))
            {
                var root=publication.RootElement; var events=root.GetProperty("identityEvents"); var receipts=root.GetProperty("receipts");
                Require(events.GetArrayLength()==1 && receipts.GetArrayLength()==1 && root.GetProperty("identityStream").GetProperty("last_sequence").GetInt64()==1,
                    "Recovered revocation duplicated or omitted its source/receipt.");
                var source=events[0];
                Require(source.GetProperty("actor_id").GetGuid()==actor && source.GetProperty("entity_id").GetGuid()==actor
                    && source.GetProperty("event_type").GetString()==expectedEvent && source.GetProperty("entity_version").GetInt64()==expectedVersion
                    && source.GetProperty("metadata").GetRawText()=="{}" && source.GetProperty("correlation_id").GetString()=="revocation-expiry-contract"
                    && receipts[0].GetProperty("session_id").GetGuid()==session,"Recovered revocation lost original attribution/session binding.");
                Require(root.GetProperty("sessions").EnumerateArray().Count(s=>s.GetProperty("revoked_at").ValueKind!=JsonValueKind.Null)==(deactivation?2:1),
                    "Recovered revocation withdrew the wrong sessions.");
                Require(deactivation ? root.GetProperty("assignments").ValueKind==JsonValueKind.Null && root.GetProperty("card").GetProperty("version").GetInt64()==2
                    && root.GetProperty("proofs").GetArrayLength()==1 && root.GetProperty("sources").GetArrayLength()==1 && root.GetProperty("routing").GetArrayLength()==1
                    && root.GetProperty("workEvents").GetArrayLength()==1 : root.GetProperty("assignments").GetArrayLength()==1 && root.GetProperty("card").GetProperty("version").GetInt64()==1
                    && root.GetProperty("proofs").ValueKind==JsonValueKind.Null && root.GetProperty("sources").ValueKind==JsonValueKind.Null && root.GetProperty("routing").ValueKind==JsonValueKind.Null,
                    "Recovered logout/deactivation changed the wrong assignment or issuer-authority state.");
            }
            Require((await Revoke(deactivation)).Succeeded && saved==await Snapshot(),"Same-key revocation replay changed canonical state.");
            var changed=await Revoke(!deactivation);
            Require(!changed.Succeeded && changed.ErrorCode=="idempotency_key_reused" && saved==await Snapshot(),"Changed revocation kind reused an original receipt.");
            context.IdempotencyKey=Guid.NewGuid(); var duplicate=await Revoke(deactivation);
            Require(!duplicate.Succeeded && duplicate.ErrorCode=="session_unavailable" && saved==await Snapshot(),"A new revocation intent reused withdrawn session authority.");
            Console.WriteLine($"Restricted PostgreSQL revocation elapsed-expiry rollback and recovery passed: deactivation={deactivation}.");
        }
    }
    private sealed class RevocationContext(Guid actor) : ICommandActorContext,IIdentityCommandContext
    {
        public bool HasHttpRequest=>true;
        public Guid? AuthenticatedUserId=>actor;
        public string? SessionTokenHash { get; set; }
        public string? RevocationSessionTokenHash=>SessionTokenHash;
        public Guid? IdempotencyKey { get; set; }
    }
}
