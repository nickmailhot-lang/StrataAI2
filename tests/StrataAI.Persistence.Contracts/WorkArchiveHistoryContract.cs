using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// PRD-18 archive-clock retention: real restricted commands and receipt replay.
// Initial records and actor admission are fixtures; this is not HTTP evidence.
internal static class WorkArchiveHistoryContract
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private sealed class Context : IWorkCommandContext { public Guid? IdempotencyKey { get; set; } }
    private sealed class Actor : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actor, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var owner = Guid.NewGuid(); var board = Guid.NewGuid();
        var list = Guid.NewGuid(); var card = Guid.NewGuid();
        var clock = new Clock { UtcNow = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow) };
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             VALUES(@owner,@email,upper(@email),'Archive history fixture','ACTIVE',true,'unused-contract-hash',@at,@at);
            INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
             VALUES(@tenant,'Archive history fixture',@owner,@at,@at);
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@owner,'OWNER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'History Board',@at,@at);
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
             VALUES(@list,@tenant,@board,'History List',lpad('1000',30,'0'),@at,@at);
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
             VALUES(@card,@tenant,@board,@list,'History Card',lpad('1000',30,'0'),@at,@at);
            """, admin))
        {
            seed.Parameters.AddWithValue("owner", owner); seed.Parameters.AddWithValue("email", $"archive-history-{owner:N}@example.test");
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("board", board);
            seed.Parameters.AddWithValue("list", list); seed.Parameters.AddWithValue("card", card); seed.Parameters.AddWithValue("at", clock.UtcNow);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var context = new Context(); var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<IClock>(clock);
        services.AddSingleton<IWorkCommandContext>(context); services.AddSingleton<ICommandActorAuthorization, Actor>();
        services.AddSingleton<PostgresBackgroundJobStore>();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract", ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }) }).Build();
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        services.AddStrataAiIdentity(settings, runtime); services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider(); var work = provider.GetRequiredService<IWorkManagementService>();
        async Task<string> Snapshot()
        {
            await using var query = new NpgsqlCommand("""
                SELECT jsonb_build_object(
                 'boards',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM boards r WHERE tenant_id=@tenant),
                 'lists',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM board_lists r WHERE tenant_id=@tenant),
                 'cards',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM cards r WHERE tenant_id=@tenant),
                 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id=@tenant),
                 'events',(SELECT count(*) FROM work_events WHERE tenant_id=@tenant),
                 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant),
                 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id=@tenant))::text;
                """, admin);
            query.Parameters.AddWithValue("tenant", tenant);
            return (string)(await query.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("History snapshot unavailable."));
        }
        async Task Verify<T>(Func<string, long, Task<WorkOperation<T>>> change,
            Func<T, (DateTimeOffset? Archive, DateTimeOffset? Deleted, Guid? Actor, long Version)> evidence)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(1); var archivedAt = clock.UtcNow;
            var archiveKey = Guid.NewGuid(); context.IdempotencyKey = archiveKey;
            var archived = await change("archive", 1); Require(archived.Succeeded && archived.Value is not null);
            Require(evidence(archived.Value!).Archive == archivedAt && evidence(archived.Value!).Version == 2);
            clock.UtcNow = clock.UtcNow.AddSeconds(1); context.IdempotencyKey = Guid.NewGuid();
            var restored = await change("restore", 2); Require(restored.Succeeded && restored.Value is not null);
            Require(evidence(restored.Value!) == (archivedAt, null, null, 3L));
            var beforeReplay = await Snapshot(); context.IdempotencyKey = archiveKey;
            var replay = await change("archive", 1);
            Require(replay.Succeeded && EqualityComparer<T>.Default.Equals(replay.Value!, archived.Value!) && await Snapshot() == beforeReplay);
            clock.UtcNow = clock.UtcNow.AddSeconds(1); var rearchivedAt = clock.UtcNow; context.IdempotencyKey = Guid.NewGuid();
            var rearchived = await change("archive", 3); Require(rearchived.Succeeded && rearchived.Value is not null);
            Require(evidence(rearchived.Value!).Archive == rearchivedAt && evidence(rearchived.Value!).Version == 4);
            clock.UtcNow = clock.UtcNow.AddSeconds(1); context.IdempotencyKey = Guid.NewGuid();
            var deleted = await change("delete", 4); Require(deleted.Succeeded && deleted.Value is not null);
            Require(evidence(deleted.Value!) == (rearchivedAt, clock.UtcNow, owner, 5L));
        }
        await Verify<CardRecord>((action, version) => work.SetCardLifecycleAsync(card, owner,
            action == "restore" ? WorkItemLifecycleState.Active : action == "archive" ? WorkItemLifecycleState.Archived : WorkItemLifecycleState.Deleted,
            version, "archive-history-card-" + action, ct, deletionConfirmed: action == "delete"),
            row => (row.ArchivedAt, row.DeletedAt, row.DeletedBy, row.Version));
        await Verify<BoardListRecord>((action, version) => work.SetListLifecycleAsync(list, owner,
            action == "restore" ? WorkItemLifecycleState.Active : action == "archive" ? WorkItemLifecycleState.Archived : WorkItemLifecycleState.Deleted,
            version, "archive-history-list-" + action, ct, deletionConfirmed: action == "delete", expectedContainedCardCount: 0),
            row => (row.ArchivedAt, row.DeletedAt, row.DeletedBy, row.Version));
        await Verify<BoardRecord>((action, version) => action == "restore" ? work.RestoreBoardAsync(board, owner, version, "archive-history-board-restore", ct)
            : action == "archive" ? work.ArchiveBoardAsync(board, owner, version, "archive-history-board-archive", ct)
            : work.DeleteBoardAsync(board, owner, version, "archive-history-board-delete", ct, deletionConfirmed: true),
            row => (row.ArchivedAt, row.DeletedAt, row.DeletedBy, row.Version));
        await VerifyParentRestoreWaits(admin, work, tenant, owner, clock, context, ct);
        Console.WriteLine("Restricted work archive history: Card/List/Board restore retains clocks, rearchive advances them, deletion retains actor/history and original archive replay changes no records or effects.");
    }
    private static async Task VerifyParentRestoreWaits(NpgsqlConnection admin, IWorkManagementService work,
        Guid tenant, Guid owner, Clock clock, Context context, CancellationToken ct)
    {
        foreach (var visibility in new[] { "PRIVATE", "ORGANIZATION", "PUBLIC" })
        foreach (var kind in new[] { "Card", "List" })
        foreach (var replay in new[] { false, true })
        {
            var board = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid();
            var target = kind == "Card" ? card : list; var table = kind == "Card" ? "cards" : "board_lists";
            clock.UtcNow = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO boards(id,tenant_id,name,visibility,created_at,updated_at)
                 VALUES(@board,@tenant,'Parent wait Board',@visibility,@at,@at);
                INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
                 VALUES(gen_random_uuid(),@tenant,@board,@owner,'ADMIN','ACTIVE',@at,@at);
                INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,archived_at,created_at,updated_at)
                 VALUES(@list,@tenant,@board,'Parent wait List',lpad('1000',30,'0'),
                  CASE WHEN @kind='List' THEN 'ARCHIVED' ELSE 'ACTIVE' END,CASE WHEN @kind='List' THEN @at ELSE NULL END,@at,@at);
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,created_at,updated_at)
                 VALUES(@card,@tenant,@board,@list,'Parent wait Card',lpad('1000',30,'0'),
                  CASE WHEN @kind='Card' THEN 'ARCHIVED' ELSE 'ACTIVE' END,CASE WHEN @kind='Card' THEN @at ELSE NULL END,@at,@at);
                """, admin))
            {
                seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("tenant", tenant);
                seed.Parameters.AddWithValue("owner", owner); seed.Parameters.AddWithValue("visibility", visibility);
                seed.Parameters.AddWithValue("list", list); seed.Parameters.AddWithValue("card", card);
                seed.Parameters.AddWithValue("kind", kind); seed.Parameters.AddWithValue("at", clock.UtcNow);
                await seed.ExecuteNonQueryAsync(ct);
            }
            async Task<bool> Change(WorkItemLifecycleState state, long version)
            {
                clock.UtcNow = clock.UtcNow.AddSeconds(1);
                if (kind == "Card") return (await work.SetCardLifecycleAsync(target, owner, state, version, "parent-wait-prepare", ct)).Succeeded;
                return (await work.SetListLifecycleAsync(target, owner, state, version, "parent-wait-prepare", ct)).Succeeded;
            }
            var restoreKey = Guid.NewGuid();
            if (replay)
            {
                context.IdempotencyKey = restoreKey; Require(await Change(WorkItemLifecycleState.Active, 1));
                context.IdempotencyKey = Guid.NewGuid(); Require(await Change(WorkItemLifecycleState.Archived, 2));
            }
            async Task<string> ProtectedState()
            {
                await using var query = new NpgsqlCommand($"""
                    SELECT jsonb_build_object(
                     'target',(SELECT to_jsonb(r) FROM {table} r WHERE tenant_id=@tenant AND id=@target),
                     'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id=@tenant AND board_id=@board),
                     'audit',(SELECT count(*) FROM audit_events WHERE tenant_id=@tenant),
                     'events',(SELECT count(*) FROM work_events WHERE tenant_id=@tenant),
                     'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant),
                     'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id=@tenant))::text;
                    """, admin);
                query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("target", target);
                query.Parameters.AddWithValue("board", board);
                return (string)(await query.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Parent wait snapshot unavailable."));
            }
            var before = await ProtectedState();
            await using var gate = (NpgsqlConnection)((ICloneable)admin).Clone(); await gate.OpenAsync(ct);
            await using var transaction = await gate.BeginTransactionAsync(ct);
            await using (var locked = new NpgsqlCommand("SELECT id FROM boards WHERE tenant_id=@tenant AND id=@board FOR UPDATE;", gate, transaction))
            {
                locked.Parameters.AddWithValue("tenant", tenant); locked.Parameters.AddWithValue("board", board);
                Require(await locked.ExecuteScalarAsync(ct) is Guid);
            }
            context.IdempotencyKey = restoreKey;
            async Task<(bool Succeeded, string? Error, bool HasValue)> Restore()
            {
                if (kind == "Card") { var result = await work.SetCardLifecycleAsync(target, owner, WorkItemLifecycleState.Active, 1, "parent-wait-restore", ct); return (result.Succeeded, result.ErrorCode, result.Value is not null); }
                var restored = await work.SetListLifecycleAsync(target, owner, WorkItemLifecycleState.Active, 1, "parent-wait-restore", ct); return (restored.Succeeded, restored.ErrorCode, restored.Value is not null);
            }
            var pending = Restore(); var released = false;
            try
            {
                var observed = false; var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (!pending.IsCompleted && DateTimeOffset.UtcNow < deadline)
                {
                    await using var waiting = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE @blocker=ANY(pg_blocking_pids(pid)) AND wait_event_type='Lock');", admin);
                    waiting.Parameters.AddWithValue("blocker", gate.ProcessID);
                    if ((bool)(await waiting.ExecuteScalarAsync(ct))!) { observed = true; break; }
                    await Task.Delay(50, ct);
                }
                Require(observed && !pending.IsCompleted);
                var parentTable = kind == "Card" ? "board_lists" : "boards";
                var parentId = kind == "Card" ? list : board;
                // Competing lifecycle commit is a controlled fixture. It retains
                // archive history; the pending restricted command is the subject.
                await using (var withdraw = new NpgsqlCommand($"""
                    UPDATE {parentTable} SET lifecycle_state='ARCHIVED',archived_at=@at,updated_at=@at,version=version+1 WHERE tenant_id=@tenant AND id=@parent;
                    UPDATE {parentTable} SET lifecycle_state='DELETED',deleted_at=@at,deleted_by=@owner,updated_at=@at,version=version+1 WHERE tenant_id=@tenant AND id=@parent;
                    """, gate, transaction))
                {
                    withdraw.Parameters.AddWithValue("at", clock.UtcNow.AddSeconds(1)); withdraw.Parameters.AddWithValue("tenant", tenant);
                    withdraw.Parameters.AddWithValue("parent", parentId); withdraw.Parameters.AddWithValue("owner", owner);
                    Require(await withdraw.ExecuteNonQueryAsync(ct) == 2);
                }
                await transaction.CommitAsync(ct); released = true;
                var refused = await pending.WaitAsync(TimeSpan.FromSeconds(30), ct);
                Require(!refused.Succeeded && !refused.HasValue && refused.Error == (kind == "Card" ? "card_not_found" : "list_not_found"));
                Require(await ProtectedState() == before);
            }
            finally
            {
                if (!released) await transaction.RollbackAsync(CancellationToken.None);
                await pending.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
        }
        Console.WriteLine("Restricted parent restore waits: 12 observed database lock waits across all Board visibilities refuse fresh Card/List restores and original restore receipts after parent deletion, without changing child records or aggregate effects.");
    }
    private static void Require(bool value)
    { if (!value) throw new InvalidOperationException("Work archive history contract failed."); }
}
