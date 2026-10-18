using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// PRD-01-VS-05-T-01/AC-01, PRD-01-TC-08: actual restricted adapters and SQL
// function entry, with observed PostgreSQL wait admission. This models admitted
// request contexts; it is not HTTP/session-cookie or deployed release evidence.
internal static class NavigationActorBoardLockContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var failures = new List<string>();
        foreach (var twoProfiles in new[] { false, true })
        foreach (var direct in new[] { false, true })
        {
            var mode = (direct ? "restricted-function" : "owning-adapter") + (twoProfiles ? "/two-profiles" : "/single-profile");
            try { await RunCase(admin, apiConnection, direct, twoProfiles, ct); Console.WriteLine($"PASS navigation/account/Board lock order: {mode}."); }
            catch (InvalidOperationException error) { failures.Add(mode); Console.WriteLine($"FAIL navigation/account/Board lock order: {mode}: {error.Message}"); }
        }
        Require(failures.Count == 0, $"Navigation/account/Board admission failed in {failures.Count}/4 controlled modes: {string.Join(", ", failures)}.");
    }

    private static async Task RunCase(NpgsqlConnection admin, string apiConnection, bool direct, bool twoProfiles, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var actor = Guid.NewGuid(); var organization = Guid.NewGuid(); var board = Guid.NewGuid();
        var list = Guid.NewGuid(); var card = Guid.NewGuid(); var request = Guid.NewGuid();
        var sessionHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Lock-order actor','ACTIVE',true,'unused-contract-hash',now(),now());
            INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at)
             VALUES(@session,@actor,@hash,clock_timestamp(),clock_timestamp()+interval '30 minutes');
            INSERT INTO organizations(id,name,owner_user_id,organization_type,created_at,updated_at)
             VALUES(@organization,'Lock-order Organization',@actor,'STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
             VALUES(@membership,@organization,@actor,'OWNER','ACTIVE',now(),now());
            INSERT INTO boards(id,tenant_id,name,visibility,created_at,updated_at)
             VALUES(@board,@organization,'Lock-order Board','PRIVATE',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
             VALUES(@list,@organization,@board,'Retained List','500000000000000000000000000000',now(),now());
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
             VALUES(@card,@organization,@board,@list,'Retained Card','500000000000000000000000000000',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"lock-order-{actor:N}@example.test");
            seed.Parameters.AddWithValue("session", Guid.NewGuid()); seed.Parameters.AddWithValue("hash", sessionHash);
            seed.Parameters.AddWithValue("organization", organization); seed.Parameters.AddWithValue("membership", Guid.NewGuid());
            seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("list", list); seed.Parameters.AddWithValue("card", card);
            await seed.ExecuteNonQueryAsync(token);
        }
        var before = await Snapshot();
        var names = new[] { $"nav-lock-work-{actor:N}", $"nav-lock-observation-{actor:N}", $"nav-lock-profile-{actor:N}", $"nav-lock-peer-profile-{actor:N}" };
        var context = new Context(actor, sessionHash);
        var logs = new DatabaseCodes();
        await using var workProvider = Provider(apiConnection, names[0], context, logs);
        await using var observationProvider = Provider(apiConnection, names[1], context, logs);
        await using var profileProvider = Provider(apiConnection, names[2], context, logs);
        await using var peerProfileProvider = Provider(apiConnection, names[3], context, logs);
        var work = workProvider.GetRequiredService<IWorkManagementService>();
        var navigation = observationProvider.GetRequiredService<NavigationInteractionReplayProducer>();
        var profile = profileProvider.GetRequiredService<IIdentityService>();
        var peerProfile = peerProfileProvider.GetRequiredService<IIdentityService>();
        await using var gate = await admin.BeginTransactionAsync(token);
        await using (var held = new NpgsqlCommand("SELECT id FROM boards WHERE tenant_id=@organization AND id=@board FOR UPDATE;", admin, gate))
        {
            held.Parameters.AddWithValue("organization", organization); held.Parameters.AddWithValue("board", board);
            Require(await held.ExecuteScalarAsync(token) is Guid, "Controlled Board gate missing.");
        }
        Task<WorkOperation<BoardSnapshot>>? read = null;
        Task<IdentityOperation<NavigationInteractionEvent>>? observed = null;
        Task<IdentityOperation<UserProfile>>? admitted = null;
        Task<IdentityOperation<UserProfile>>? peerAdmitted = null;
        var running = new List<Task>();
        var released = false;
        try
        {
            read = Task.Run(() => work.GetBoardAsync(board, actor, token), token); running.Add(read);
            await Waiting(names[0], read);
            observed = Task.Run(() => Observe(), token); running.Add(observed);
            await Waiting(names[1], observed);
            admitted = Task.Run(() => profile.ReadProfileAsync(actor, token), token); running.Add(admitted);
            await Waiting(names[2], admitted, allowCompleted: true);
            if (twoProfiles)
            {
                // The exact CI cycle has TWO exclusive account admissions.
                // A second queued profile read preserves that four-way order;
                // the original single-profile cases remain positive controls.
                peerAdmitted = Task.Run(() => peerProfile.ReadProfileAsync(actor, token), token); running.Add(peerAdmitted);
                await Waiting(names[3], peerAdmitted, allowCompleted: true);
            }
            await gate.CommitAsync(token); released = true;
            await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(15), token);
            var result = await read; var eventResult = await observed; var accountResult = await admitted;
            var peerResult = peerAdmitted is null ? null : await peerAdmitted;
            Console.WriteLine($"Observed ordered contenders: direct={direct}; twoProfiles={twoProfiles}; Board={result.ErrorCode ?? "success"}; navigation={eventResult.ErrorCode ?? "success"}; profile={accountResult.ErrorCode ?? "success"}; peerProfile={peerResult?.ErrorCode ?? "success"}; database={string.Join(",", logs.Codes)}.");
            Require(result.Succeeded && eventResult.Succeeded && accountResult.Succeeded && (peerResult is null || peerResult.Succeeded),
                "Concurrent admitted reads did not all succeed; original callbacks/status expectations are retained.");
            Require(!logs.Codes.Contains("40P01"), "Actual restricted account/Board operations deadlocked.");
            Require(before == await Snapshot(), "Read/navigation/profile admission changed the canonical graph or account.");
            var replay = await Observe();
            Require(replay.Succeeded && replay.Value == eventResult.Value, "Original navigation replay changed identity/clock or was unavailable.");
            await using var proof = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM navigation_interaction_events WHERE actor_id=@actor)=1
                 AND (SELECT count(*) FROM navigation_interaction_replays WHERE actor_id=@actor AND request_id=@request)=1
                 AND (SELECT count(*) FROM work_events WHERE tenant_id=@organization)=0
                 AND (SELECT count(*) FROM audit_events WHERE tenant_id=@organization)=0;
                """, admin);
            proof.Parameters.AddWithValue("actor", actor); proof.Parameters.AddWithValue("request", request);
            proof.Parameters.AddWithValue("organization", organization);
            Require(await proof.ExecuteScalarAsync(token) is true, "Navigation replay duplicated history or invented work/audit mutations.");
        }
        finally
        {
            if (!released) await gate.RollbackAsync(CancellationToken.None);
            if (running.Count > 0)
            {
                try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                catch { deadline.Cancel(); }
            }
        }

        async Task<IdentityOperation<NavigationInteractionEvent>> Observe()
        {
            if (!direct) return await navigation.BoardAsync(actor, organization, board, 1, request, new string('a', 64), token);
            await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(apiConnection) { ApplicationName = names[1] }.ConnectionString);
            await connection.OpenAsync(token); await using var tx = await connection.BeginTransactionAsync(token);
            await using (var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@actor,true);", connection, tx))
            { subject.Parameters.AddWithValue("actor", actor.ToString("D")); await subject.ExecuteNonQueryAsync(token); }
            var candidate = Guid.NewGuid(); var created = DateTimeOffset.UtcNow;
            await using var query = new NpgsqlCommand("""
                SELECT original_event,original_created FROM public.append_or_replay_navigation_interaction(
                 @request,@fingerprint,@event,@actor,'BOARD_OPENED',@organization,@board,@board,1,@created);
                """, connection, tx);
            query.Parameters.AddWithValue("request", request); query.Parameters.AddWithValue("fingerprint", new string('a', 64));
            query.Parameters.AddWithValue("event", candidate); query.Parameters.AddWithValue("actor", actor);
            query.Parameters.AddWithValue("organization", organization); query.Parameters.AddWithValue("board", board);
            query.Parameters.AddWithValue("created", created);
            try
            {
                NavigationInteractionEvent? original;
                await using (var reader = await query.ExecuteReaderAsync(token))
                { original = await reader.ReadAsync(token) ? NavigationInteractionEvent.BoardOpened(reader.GetGuid(0), actor, organization, board, 1, new DateTimeOffset(reader.GetDateTime(1))) : null; }
                await tx.CommitAsync(token);
                return original is null ? IdentityOperation<NavigationInteractionEvent>.Failure("navigation_unavailable") : IdentityOperation<NavigationInteractionEvent>.Success(original);
            }
            catch (PostgresException error) when (error.SqlState == "40P01")
            { logs.Codes.Enqueue(error.SqlState); return IdentityOperation<NavigationInteractionEvent>.Failure("identity_storage_unavailable"); }
        }

        async Task Waiting(string name, Task operation, bool allowCompleted = false)
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < until)
            {
                if (operation.IsCompleted)
                { Require(allowCompleted, $"Contender phase {Array.IndexOf(names, name)} completed before its controlled wait; state={operation.Status}."); return; }
                await using (var clear = new NpgsqlCommand("SELECT pg_stat_clear_snapshot();", admin, gate)) await clear.ExecuteNonQueryAsync(token);
                await using var wait = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name=@name
                     AND state='active' AND wait_event_type='Lock' AND cardinality(pg_blocking_pids(pid))>0);
                    """, admin, gate);
                wait.Parameters.AddWithValue("name", name);
                if (await wait.ExecuteScalarAsync(token) is true)
                {
                    await using var shape = new NpgsqlCommand("SELECT query FROM pg_stat_activity WHERE application_name=@name AND state='active' AND wait_event_type='Lock';", admin, gate);
                    shape.Parameters.AddWithValue("name", name);
                    var sql = (string?)await shape.ExecuteScalarAsync(token) ?? "";
                    var kind = sql.Contains("runtime_database_role_is_safe", StringComparison.Ordinal) ? "runtime-role-admission" : sql.Contains("append_or_replay_navigation", StringComparison.Ordinal) ? "navigation-function" : sql.Contains("users", StringComparison.Ordinal) ? "account-row" : sql.Contains("boards", StringComparison.Ordinal) ? "Board-row" : "other";
                    Console.WriteLine($"Actual wait: twoProfiles={twoProfiles}; direct={direct}; phase={Array.IndexOf(names, name)}; queryKind={kind}.");
                    return;
                }
                await Task.Delay(20, token);
            }
            throw new InvalidOperationException("Actual controlled PostgreSQL wait was not observed.");
        }
        async Task<string> Snapshot()
        {
            await using var query = new NpgsqlCommand("""
                SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id=@actor),
                 'board',(SELECT to_jsonb(b) FROM boards b WHERE tenant_id=@organization AND id=@board),
                 'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id=@organization),
                 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id=@organization))::text;
                """, admin);
            query.Parameters.AddWithValue("actor", actor); query.Parameters.AddWithValue("organization", organization); query.Parameters.AddWithValue("board", board);
            return (string)(await query.ExecuteScalarAsync(token))!;
        }
    }

    private static ServiceProvider Provider(string connection, string name, Context context, DatabaseCodes logs)
    {
        var services = new ServiceCollection(); services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton(new PostgresConnectionFactory(new NpgsqlConnectionStringBuilder(connection) { ApplicationName = name }.ConnectionString));
        services.AddSingleton<IClock, SystemClock>(); services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ICommandActorContext>(context); services.AddSingleton<IIdentityCommandContext>(context); services.AddSingleton<IWorkCommandContext>(context);
        services.AddSingleton<ICommandActorAuthorization, CommandActorAuthorization>(); services.AddSingleton<PostgresBackgroundJobStore>();
        services.AddSingleton<IBackgroundJobStore>(provider => provider.GetRequiredService<PostgresBackgroundJobStore>());
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL"] = "false", ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract",
            ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }),
        }).Build();
        services.AddStrataAiIdentity(configuration, runtime); services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        return services.BuildServiceProvider();
    }
    private sealed class Context(Guid actor, string hash) : ICommandActorContext, IIdentityCommandContext, IWorkCommandContext
    {
        public bool HasHttpRequest => true;
        public Guid? AuthenticatedUserId => actor;
        public string? SessionTokenHash => hash;
        public string? RevocationSessionTokenHash => hash;
        public Guid? IdempotencyKey => null;
    }
    private sealed class DatabaseCodes : ILoggerProvider
    {
        public ConcurrentQueue<string> Codes { get; } = new();
        public ILogger CreateLogger(string categoryName) => new DatabaseLogger(Codes);
        public void Dispose() { }
        private sealed class DatabaseLogger(ConcurrentQueue<string> codes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                    foreach (var value in values.Where(x => x.Key == "DatabaseCode")) if (value.Value is string code) codes.Enqueue(code);
            }
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
