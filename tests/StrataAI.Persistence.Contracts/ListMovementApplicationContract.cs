using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.Records;
using StrataAI.Infrastructure.WorkManagement;

// Actual registered production orchestration and restricted persistence.
// Actor/session admission is controlled; this is not an HTTP session fixture.
internal static class ListMovementApplicationContract
{
    private sealed class ActorFence : ICommandActorAuthorization
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken ct = default) => Task.FromResult(Allowed);
    }
    private sealed class Context : IWorkCommandContext { public Guid? IdempotencyKey { get; set; } = Guid.NewGuid(); }
    private sealed class LateActorLoss(IWorkEventStore inner, ActorFence actors, Guid source) : IWorkEventStore
    {
        public bool Enabled { get; set; }
        public async Task AppendAsync(WorkEvent change, CancellationToken ct = default)
        { await inner.AppendAsync(change, ct); if (Enabled && change.EventType == "LIST_MOVED" && change.BoardId == source) actors.Allowed = false; }
        public async Task AppendBatchAsync(IReadOnlyList<WorkEvent> changes, CancellationToken ct = default)
        {
            await inner.AppendBatchAsync(changes, ct);
            if (Enabled && changes.Any(e => e.EventType == "LIST_MOVED" && e.BoardId == source)) actors.Allowed = false;
        }
    }

    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var member = Guid.NewGuid();
        var source = Guid.NewGuid(); var destination = Guid.NewGuid(); var list = Guid.NewGuid();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task Sql(string text) { await using var q = new NpgsqlCommand(text, admin); await q.ExecuteNonQueryAsync(ct); }
        async Task<string> Scalar(string text) { await using var q = new NpgsqlCommand(text, admin); return (await q.ExecuteScalarAsync(ct))?.ToString() ?? ""; }
        await Sql($$"""
            INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
            SELECT u,u::text||'@example.test',upper(u::text||'@example.test'),'Application actor','unusable-fixture','ACTIVE',true,now(),now()
            FROM unnest(ARRAY['{{actor}}'::uuid,'{{member}}'::uuid]) u;
            INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
            VALUES ('{{tenant}}','Application fixture','{{actor}}','ACTIVE','STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}',u,CASE WHEN u='{{actor}}'::uuid THEN 'OWNER' ELSE 'MEMBER' END,'ACTIVE',now(),now()
            FROM unnest(ARRAY['{{actor}}'::uuid,'{{member}}'::uuid]) u;
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
            VALUES ('{{source}}','{{tenant}}','Source',now(),now()),('{{destination}}','{{tenant}}','Destination',now(),now());
            INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
            VALUES (gen_random_uuid(),'{{tenant}}','{{source}}','{{member}}','MEMBER','ACTIVE',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
            VALUES ('{{list}}','{{tenant}}','{{source}}','Whole List',repeat('5',30),now(),now());
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,archived_at,deleted_at,deleted_by,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}','{{source}}','{{list}}','Stable Card','Stable body',repeat(n::text,30),
                CASE n WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,
                CASE WHEN n>1 THEN now() END,CASE WHEN n=3 THEN now() END,CASE WHEN n=3 THEN '{{actor}}'::uuid END,now(),now()
            FROM generate_series(1,3) n;
            """);
        string GraphHash() => $$"""
            SELECT md5(jsonb_build_object(
              'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id='{{tenant}}'),
              'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='{{tenant}}'),
              'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='{{tenant}}'),
              'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id='{{tenant}}'),
              'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY board_id) FROM work_event_streams s WHERE tenant_id='{{tenant}}'),
              'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='{{tenant}}'),
              'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM work_command_replays r WHERE tenant_id='{{tenant}}'))::text);
            """;
        var actors = new ActorFence(); var context = new Context();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<IClock, SystemClock>(); services.AddSingleton<PostgresBackgroundJobStore>();
        services.AddSingleton(new IdentityPolicy(false, true, 12, TimeSpan.FromDays(1), TimeSpan.FromMinutes(30)));
        services.AddSingleton<ICommandActorAuthorization>(actors); services.AddSingleton<IWorkCommandContext>(context);
        services.AddSingleton<IIdentityStore, PostgresIdentityStore>();
        services.AddSingleton<IOrganizationStore, PostgresOrganizationStore>();
        services.AddSingleton<IRetentionWorkDisposition, PostgresRetentionWorkDisposition>();
        services.AddStrataAiWorkManagement(new(RuntimeMode.Production, "contract", "contract"));
        services.AddSingleton<LateActorLoss>(p => new(new PostgresWorkEventStore(p.GetRequiredService<PostgresConnectionFactory>(),
            p.GetRequiredService<PostgresBackgroundJobStore>()), actors, source));
        services.AddSingleton<IWorkEventStore>(p => p.GetRequiredService<LateActorLoss>());
        await using var provider = services.BuildServiceProvider();
        var movement = provider.GetRequiredService<ListBoardMovementService>();
        var before = await Scalar(GraphHash());
        var denied = await movement.MoveAsync(list, member, source, destination, 999, null, "list-app-test", ct);
        Require(denied.ErrorCode == "list_not_found", "Destination refusal disclosed a version conflict.");
        Require(await Scalar(GraphHash()) == before, "Unauthorized movement changed protected state.");
        var loss = provider.GetRequiredService<LateActorLoss>(); loss.Enabled = true;
        var refused = await movement.MoveAsync(list, actor, source, destination, 1, null, "list-app-test", ct);
        Require(refused.ErrorCode == "session_unavailable", "Late actor loss did not reject movement.");
        Require(await Scalar(GraphHash()) == before, "Late actor loss retained graph, audit, events, jobs or receipt.");
        actors.Allowed = true; loss.Enabled = false;
        var success = await movement.MoveAsync(list, actor, source, destination, 1, null, "list-app-test", ct);
        Require(success.Succeeded && success.Value is { Version: 2 } && success.Value.BoardId == destination, "Original-key retry failed.");
        Require(await Scalar($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}';") == "4", "Complete List/Card audit count differs.");
        Require(await Scalar($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == "8", "Both Board streams omitted List/Card events.");
        Require(await Scalar($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='WORK_EVENT_READY';") == "8", "Movement omitted delivery jobs.");
        Require(await Scalar($"SELECT count(*) FROM work_command_replays WHERE tenant_id='{tenant}' AND result_json IS NOT NULL;") == "1", "Original success receipt was not retained once.");
        Require(await Scalar($"SELECT count(*) FROM cards WHERE tenant_id='{tenant}' AND board_id='{destination}' AND version=2;") == "3", "Movement lost a contained lifecycle.");
        var committed = await Scalar(GraphHash());
        Require(await movement.MoveAsync(list, actor, source, destination, 1, null, "list-app-test", ct) == success, "Original receipt replay differs.");
        Require(await Scalar(GraphHash()) == committed, "Replay reapplied movement or publication.");
        Require((await movement.MoveAsync(list, actor, source, destination, 1, Guid.NewGuid(), "list-app-test", ct)).ErrorCode == "idempotency_key_reused", "Changed replay body was admitted.");
        Require(await Scalar(GraphHash()) == committed, "Changed replay mutated protected state.");
        await Sql($"UPDATE boards SET lifecycle_state='ARCHIVED',archived_at=now(),updated_at=now(),version=version+1 WHERE id='{source}';");
        var withdrawn = await Scalar(GraphHash());
        Require((await movement.MoveAsync(list, actor, source, destination, 1, null, "list-app-test", ct)).ErrorCode == "list_not_found", "Replay ignored original source lifecycle withdrawal.");
        Require(await Scalar(GraphHash()) == withdrawn, "Withdrawn replay changed protected state.");
        Console.WriteLine("List movement Application: actual production registration, restricted API role, three lifecycles, current Board authorization, late actor rollback, original-key retry/replay and source withdrawal passed; session admission controlled.");
    }
}
