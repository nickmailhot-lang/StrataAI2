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
        Console.WriteLine("Restricted work archive history: Card/List/Board restore retains clocks, rearchive advances them, deletion retains actor/history and original archive replay changes no records or effects.");
    }
    private static void Require(bool value)
    { if (!value) throw new InvalidOperationException("Work archive history contract failed."); }
}
