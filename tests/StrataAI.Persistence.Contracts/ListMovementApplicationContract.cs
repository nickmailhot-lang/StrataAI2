using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.BackgroundJobs;
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
    private sealed class PublicationClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(DateTimeOffset.UtcNow.AddMinutes(1).UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
    }
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

    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        await RunCaseAsync(admin, apiConnection, workerConnection, true, ct);
        await RunCaseAsync(admin, apiConnection, workerConnection, false, ct);
    }

    private static async Task RunCaseAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, bool eligible, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var member = Guid.NewGuid(); var recipient = Guid.NewGuid();
        var source = Guid.NewGuid(); var destination = Guid.NewGuid(); var list = Guid.NewGuid();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task Sql(string text) { await using var q = new NpgsqlCommand(text, admin); await q.ExecuteNonQueryAsync(ct); }
        async Task<string> Scalar(string text) { await using var q = new NpgsqlCommand(text, admin); return (await q.ExecuteScalarAsync(ct))?.ToString() ?? ""; }
        await Sql($$"""
            INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
            SELECT u,u::text||'@example.test',upper(u::text||'@example.test'),'Application actor','unusable-fixture','ACTIVE',true,now(),now()
            FROM unnest(ARRAY['{{actor}}'::uuid,'{{member}}'::uuid,'{{recipient}}'::uuid]) u;
            INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
            VALUES ('{{tenant}}','Application fixture','{{actor}}','ACTIVE','STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}',u,CASE WHEN u='{{actor}}'::uuid THEN 'OWNER' ELSE 'MEMBER' END,'ACTIVE',now(),now()
            FROM unnest(ARRAY['{{actor}}'::uuid,'{{member}}'::uuid,'{{recipient}}'::uuid]) u;
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
            VALUES ('{{source}}','{{tenant}}','Source',now(),now()),('{{destination}}','{{tenant}}','Destination',now(),now());
            INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
            VALUES (gen_random_uuid(),'{{tenant}}','{{source}}','{{member}}','MEMBER','ACTIVE',now(),now()),
                (gen_random_uuid(),'{{tenant}}','{{source}}','{{recipient}}','MEMBER','ACTIVE',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
            VALUES ('{{list}}','{{tenant}}','{{source}}','Whole List',repeat('5',30),now(),now());
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,archived_at,deleted_at,deleted_by,
                due_at,due_timezone,due_has_time,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}','{{source}}','{{list}}','Stable Card','Stable body',repeat(n::text,30),
                CASE n WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,
                CASE WHEN n>1 THEN now() END,CASE WHEN n=3 THEN now() END,CASE WHEN n=3 THEN '{{actor}}'::uuid END,
                now()+interval '30 minutes','UTC',true,now(),now()
            FROM generate_series(1,3) n;
            INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,card_id,watching,created_at,updated_at,version)
            SELECT tenant_id,gen_random_uuid(),'{{recipient}}','CARD',id,id,true,now(),now(),1 FROM cards WHERE tenant_id='{{tenant}}';
            INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,list_id,watching,created_at,updated_at,version)
            VALUES ('{{tenant}}',gen_random_uuid(),'{{recipient}}','LIST','{{list}}','{{list}}',true,now(),now(),1),
              ('{{tenant}}',gen_random_uuid(),'{{actor}}','LIST','{{list}}','{{list}}',true,now(),now(),1);
            INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,board_id,watching,created_at,updated_at,version)
            VALUES ('{{tenant}}',gen_random_uuid(),'{{recipient}}','BOARD','{{source}}','{{source}}',true,now(),now(),1),
              ('{{tenant}}',gen_random_uuid(),'{{member}}','BOARD','{{source}}','{{source}}',true,now(),now(),1);
            INSERT INTO card_reminders(tenant_id,id,user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,created_at,updated_at,version)
            SELECT tenant_id,gen_random_uuid(),'{{recipient}}',id,'1_HOUR',true,due_at,due_at-interval '1 hour','SCHEDULED',1,now(),now(),1
            FROM cards WHERE tenant_id='{{tenant}}' AND lifecycle_state<>'DELETED';
            """);
        if (eligible) await Sql($$"""
            INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
            VALUES (gen_random_uuid(),'{{tenant}}','{{destination}}','{{recipient}}','MEMBER','ACTIVE',now(),now());
            INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,board_id,watching,created_at,updated_at,version)
            VALUES ('{{tenant}}',gen_random_uuid(),'{{recipient}}','BOARD','{{destination}}','{{destination}}',true,now(),now(),1);
            """);
        string GraphHash() => $$"""
            SELECT md5(jsonb_build_object(
              'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id='{{tenant}}'),
              'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='{{tenant}}'),
              'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='{{tenant}}'),
              'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id='{{tenant}}'),
              'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY board_id) FROM work_event_streams s WHERE tenant_id='{{tenant}}'),
              'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='{{tenant}}'),
              'reminders',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM card_reminders r WHERE tenant_id='{{tenant}}'),
              'watches',(SELECT jsonb_agg(to_jsonb(w) ORDER BY id) FROM watch_subscriptions w WHERE tenant_id='{{tenant}}'),
              'notices',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='{{tenant}}'),
              'noticeStreams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) FROM notification_event_streams s WHERE tenant_id='{{tenant}}'),
              'noticeEvents',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM notification_events e WHERE tenant_id='{{tenant}}'),
              'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM work_command_replays r WHERE tenant_id='{{tenant}}'))::text);
            """;
        var actors = new ActorFence(); var context = new Context();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<IClock, PublicationClock>(); services.AddSingleton<PostgresBackgroundJobStore>();
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
        var connections = provider.GetRequiredService<PostgresConnectionFactory>();
        var reminderStore = provider.GetRequiredService<ICardReminderStore>();
        var activeCard = Guid.Parse(await Scalar($"SELECT id FROM cards WHERE tenant_id='{tenant}' AND lifecycle_state='ACTIVE';"));
        var activeReminderBefore = await Scalar($"SELECT to_jsonb(r)::text FROM card_reminders r WHERE tenant_id='{tenant}' AND card_id='{activeCard}';");
        await connections.ExecuteTenantCommandAsync(tenant, async () =>
        {
            foreach (var id in new[] { activeCard, Guid.Parse(await Scalar($"SELECT id FROM cards WHERE tenant_id='{tenant}' AND lifecycle_state='ARCHIVED';")) })
                await provider.GetRequiredService<ICardReminderJobPublisher>().PublishAsync((await reminderStore.FindAsync(tenant, recipient, id, ct))!, recipient, "list-app-test", ct);
            return true;
        }, value => value, ct);
        Require(await Scalar($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='CARD_REMINDER';") == "2", "Fixture did not queue both original overdue Reminder jobs.");
        var queuedBefore = await Scalar($"SELECT jsonb_agg(to_jsonb(j) ORDER BY id)::text FROM background_jobs j WHERE tenant_id='{tenant}' AND job_type='CARD_REMINDER';");
        var originalJobs = new HashSet<Guid>();
        await using (var q = new NpgsqlCommand($"SELECT id FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='CARD_REMINDER';", admin))
        await using (var r = await q.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) originalJobs.Add(r.GetGuid(0));
        var before = await Scalar(GraphHash());
        var denied = await movement.MoveAsync(list, member, source, destination, 999, null, "list-app-test", ct);
        Require(denied.ErrorCode == "list_not_found", "Destination refusal disclosed a version conflict.");
        Require(await Scalar(GraphHash()) == before, "Unauthorized movement changed protected state.");
        await connections.ExecuteTenantCommandAsync(tenant, async () =>
        {
            var transferred = await provider.GetRequiredService<IListBoardMovementStore>().TransferListBoardAsync(tenant, source, list,
                destination, 1, provider.GetRequiredService<IClock>().UtcNow, true, null, ct);
            Require(transferred is not null && transferred.List == await provider.GetRequiredService<IWorkManagementStore>().FindListAsync(list, ct),
                "Store acknowledgment differs from the persisted List timestamp.");
            return false;
        }, value => value, ct);
        Require(await Scalar(GraphHash()) == before, "Acknowledgment probe retained a transfer.");
        var loss = provider.GetRequiredService<LateActorLoss>(); loss.Enabled = true;
        var refused = await movement.MoveAsync(list, actor, source, destination, 1, null, "list-app-test", ct);
        Require(refused.ErrorCode == "session_unavailable", "Late actor loss did not reject movement.");
        Require(await Scalar(GraphHash()) == before, "Late actor loss retained graph, audit, events, jobs or receipt.");
        actors.Allowed = true; loss.Enabled = false;
        var success = await movement.MoveAsync(list, actor, source, destination, 1, null, "list-app-test", ct);
        Require(success.Succeeded && success.Value is { Version: 2 } && success.Value.BoardId == destination, "Original-key retry failed.");
        var changedReminders = eligible ? 1 : 2;
        Require(await Scalar($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}';") == (4 + changedReminders).ToString(), "Complete List/Card/Reminder audit count differs.");
        Require(await Scalar($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == (8 + changedReminders).ToString(), "Both Board streams omitted List/Card/Reminder events.");
        Require(await Scalar($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='WORK_EVENT_READY';") == (8 + changedReminders).ToString(), "Movement omitted delivery jobs.");
        Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}';") == (eligible ? "1" : "0"), "Watch deduplication or destination admission failed.");
        Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}' AND recipient_id IN ('{actor}','{member}');") == "0", "Actor/source-only Board watch received a move notice.");
        if (eligible)
        {
            Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}' AND recipient_id='{recipient}' AND board_id='{destination}' AND card_id='{activeCard}' AND notification_type='CARD_MOVED';") == "1", "Eligible notice did not bind the active destination Card.");
            Require(await Scalar($"SELECT to_jsonb(r)::text FROM card_reminders r WHERE tenant_id='{tenant}' AND card_id='{activeCard}';") == activeReminderBefore, "Eligible overdue Reminder choice changed.");
        }
        Require(await Scalar($"SELECT count(*) FROM card_reminders WHERE tenant_id='{tenant}' AND status='SUSPENDED' AND generation=2 AND version=2 AND trigger_at IS NULL;") == changedReminders.ToString(), "Hidden/ineligible Reminder remained scheduled.");
        Require(await Scalar($"SELECT jsonb_agg(to_jsonb(j) ORDER BY id)::text FROM background_jobs j WHERE tenant_id='{tenant}' AND job_type='CARD_REMINDER';") == queuedBefore, "Move replaced or altered original overdue jobs.");
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
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var queue = new PostgresBackgroundJobStore(worker); var workerId = Guid.NewGuid();
        var delivery = new PostgresCardReminderDeliveryStore(worker, requireVerifiedEmail: true);
        var delivered = 0; var superseded = 0;
        for (var n = 0; n < 2; n++)
        {
            var claim = await queue.ClaimAsync(tenant, workerId, ct);
            Require(claim is not null && claim.JobType == CardReminderDeliveryHandler.Type && originalJobs.Remove(claim.Id),
                "Worker did not claim an original overdue Reminder job.");
            var attempt = CardReminderAttempt.Parse(claim!.SafeMetadataJson);
            var result = await delivery.DeliverAsync(claim, attempt, ct);
            Require(result is CardReminderDeliveryResult.Delivered or CardReminderDeliveryResult.Superseded, "Original Reminder delivery lost its lease.");
            var effects = await Scalar(GraphHash());
            Require(await delivery.DeliverAsync(claim, attempt, ct) == result && await Scalar(GraphHash()) == effects,
                "Live-lease Reminder redelivery repeated its effect.");
            if (result == CardReminderDeliveryResult.Delivered) delivered++; else superseded++;
            Require(await queue.CompleteAsync(tenant, claim.Id, claim.LeaseId, workerId, ct), "Worker did not acknowledge original Reminder delivery.");
            var acknowledged = await Scalar(GraphHash());
            Require(await delivery.DeliverAsync(claim, attempt, ct) == CardReminderDeliveryResult.LeaseLost
                && await Scalar(GraphHash()) == acknowledged, "Completed Reminder lease produced another effect.");
        }
        Require(originalJobs.Count == 0 && delivered == (eligible ? 1 : 0) && superseded == (eligible ? 1 : 2),
            "Worker delivered a hidden/ineligible Reminder or lost the eligible original job.");
        Require(await Scalar($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='CARD_REMINDER' AND state='SUCCEEDED';") == "2",
            "Worker did not retain both original successful acknowledgments.");
        Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}' AND notification_type='REMINDER_FIRED';") == (eligible ? "1" : "0"),
            "Reminder delivery duplicated or leaked its private notice.");
        if (eligible)
        {
            Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}' AND notification_type='REMINDER_FIRED' AND board_id='{destination}' AND card_id='{activeCard}' AND recipient_id='{recipient}';") == "1",
                "Original Reminder delivered against the former source Board.");
            Require(await Scalar($"SELECT count(*) FROM card_reminders WHERE tenant_id='{tenant}' AND card_id='{activeCard}' AND status='FIRED' AND generation=1 AND version=2;") == "1",
                "Original eligible Reminder did not fire exactly once.");
        }
        Require(await Scalar($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}' AND event_type='REMINDER_FIRED';") == (eligible ? "1" : "0"),
            "Original delivery lost or repeated its audit fact.");
        Require(await Scalar($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}' AND event_type='REMINDER_FIRED' AND board_id='{destination}';") == (eligible ? "1" : "0"),
            "Original delivery lost or repeated its destination event.");
        Console.WriteLine($"List movement Application: three lifecycles, populated watches/overdue Reminders, destination eligibility {eligible}, complete late rollback, original-key retry/replay and source withdrawal passed; session admission controlled.");
        Console.WriteLine($"Original Reminder Worker delivery: {delivered} delivered, {superseded} superseded; live-lease retry unchanged, original acknowledgment retained, completed lease withdrawn.");
    }
}
