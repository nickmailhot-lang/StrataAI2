using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
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
using StrataAI.Domain.WorkManagement;

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
    private sealed class FileObjects(AttachmentObjectReference reference, byte[] bytes) : IAttachmentObjectStorage
    {
        private readonly HashSet<AttachmentObjectReference> _references = [reference];
        public void AddPreview(AttachmentObjectReference preview) => _references.Add(preview);
        public int Reads { get; private set; }
        public AttachmentObjectReference? LastRead { get; private set; }
        public Func<Task>? AfterRead { get; set; }
        public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference target, Stream source, long maximumBytes, CancellationToken ct)
            => throw new InvalidOperationException("Read-only object fixture.");
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference target, CancellationToken ct) => Task.FromResult(false);
        public async Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference target, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Reads++; LastRead = target;
            if (!_references.Contains(target)) return null;
            if (AfterRead is { } after) await after();
            return new MemoryStream(bytes, writable: false);
        }
    }
    private sealed class PublicationClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(DateTimeOffset.UtcNow.AddMinutes(1).UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
    }
    private sealed class CleanScanner : IAttachmentMalwareScanner
    {
        public async Task<AttachmentScannerVerdict> ScanAsync(Stream source, CancellationToken ct)
        { await source.CopyToAsync(Stream.Null, ct); return AttachmentScannerVerdict.Clean; }
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
              'files',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE tenant_id='{{tenant}}'),
              'uploads',(SELECT jsonb_agg(to_jsonb(u) ORDER BY id) FROM attachment_upload_intents u WHERE tenant_id='{{tenant}}'),
              'previews',(SELECT jsonb_agg(to_jsonb(p) ORDER BY id) FROM attachment_previews p WHERE tenant_id='{{tenant}}'),
              'publications',(SELECT jsonb_agg(to_jsonb(p) ORDER BY id) FROM attachment_preview_publications p WHERE tenant_id='{{tenant}}'),
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
        var file = Guid.NewGuid(); var reference = new AttachmentObjectReference(tenant, file);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9xkAAAAASUVORK5CYII=");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        // Measured storage claims are fixtures. Persist real upload transitions,
        // Pending metadata and a scan job through owning restricted adapters.
        var at = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
        var uploads = provider.GetRequiredService<IAttachmentUploadIntentStore>();
        var metadata = provider.GetRequiredService<IAttachmentMetadataStore>();
        var objects = new FileObjects(reference, bytes);
        await connections.ExecuteTenantCommandAsync(tenant, async () =>
        {
            var intent = AttachmentUploadIntent.Prepare(file, tenant, activeCard, actor, Guid.NewGuid(), 1,
                "Private file", bytes.Length, digest, at.AddHours(1), at);
            Require(await uploads.PrepareUploadAsync(intent, ct) is not null, "Upload prepare failed.");
            var lease = Guid.NewGuid();
            Require(await uploads.TryChangeUploadAsync(tenant, activeCard, actor, file, 1,
                new(AttachmentUploadAction.StartWrite, at, lease, at.AddMinutes(5)), ct) is not null, "Upload writer failed.");
            Require(await uploads.TryChangeUploadAsync(tenant, activeCard, actor, file, 2,
                new(AttachmentUploadAction.RecordStored, at, lease, Measured: new(reference, bytes.Length, digest), VerifiedMimeType: "image/png"), ct)
                is not null, "Upload measurement failed.");
            await metadata.CreateFileAttachmentAsync(new(reference, bytes.Length, digest), activeCard, actor, "Private file", "image/png", at, ct);
            var published = await uploads.TryChangeUploadAsync(tenant, activeCard, actor, file, 3, new(AttachmentUploadAction.Publish, at), ct);
            var pending = await metadata.FindFileAttachmentAsync(tenant, activeCard, file, ct);
            Require(published is not null && pending is not null && await provider.GetRequiredService<IAttachmentScanJobPublisher>()
                .PublishScanAsync(published, pending, actor, "list-app-scan", ct), "Upload scan publication failed.");
            return true;
        }, value => value, ct);
        Guid previewId;
        await using (var previewWorker = new PostgresConnectionFactory(workerConnection))
        {
            var previewQueue = new PostgresBackgroundJobStore(previewWorker, previewJobs: true);
            var previewWorkerId = Guid.NewGuid();
            var scanClaim = await previewQueue.ClaimAsync(tenant, previewWorkerId, ct);
            Require(scanClaim is not null && scanClaim.JobType == AttachmentScanJobs.Type
                && AttachmentScanAttempt.Parse(scanClaim.SafeMetadataJson).AttachmentId == file, "Worker did not claim the original scan.");
            await new AttachmentScanDeliveryHandler(new PostgresAttachmentScanDeliveryStore(previewWorker, previewEnabled: true),
                new AttachmentQuarantineScanner(objects, new CleanScanner())).ExecuteAsync(scanClaim!, ct);
            Require(await previewQueue.CompleteAsync(tenant, scanClaim!.Id, scanClaim.LeaseId, previewWorkerId, ct), "Scan acknowledgment failed.");
            var claim = await previewQueue.ClaimAsync(tenant, previewWorkerId, ct);
            Require(claim is not null && claim.JobType == AttachmentPreviewJobs.Type, "Preview Worker did not claim the automatically published job.");
            previewId = claim!.Id;
            var attempt = AttachmentPreviewAttempt.Parse(claim!.SafeMetadataJson);
            var publication = new PostgresAttachmentPreviewIntentStore(previewWorker);
            var loaded = await publication.LoadAsync(claim, attempt, ct);
            Require(loaded.Status == AttachmentPreviewLoadStatus.Ready && loaded.Source is not null, "Preview source was not admitted.");
            var measurement = new AttachmentPreviewMeasurement(bytes.Length, digest, 1, 1);
            Require(await publication.DeclareAsync(claim, attempt, loaded.Source!, "image/png", measurement, ct) == AttachmentPreviewDeclaration.Declared,
                "Preview declaration was not persisted.");
            var previewReference = AttachmentObjectReference.ForPreview(tenant, previewId); objects.AddPreview(previewReference);
            Require(await publication.FinishAsync(claim, attempt, new AttachmentPreviewStoredOutput(previewReference, measurement), ct) == AttachmentPreviewCompletion.Applied,
                "Actual Worker preview publication failed.");
            Require(await previewQueue.CompleteAsync(tenant, claim.Id, claim.LeaseId, previewWorkerId, ct), "Preview Worker acknowledgment failed.");
        }
        var movementKey = context.IdempotencyKey; context.IdempotencyKey = Guid.NewGuid();
        var coverCard = (await provider.GetRequiredService<IWorkManagementStore>().FindCardAsync(activeCard, ct))!;
        Require((await provider.GetRequiredService<CardAttachmentCoverService>().SetAsync(activeCard, actor,
            new SetCardCoverInput(file, coverCard.Version, 3), "list-app-cover", ct)).Succeeded, "Actual cover selection command failed.");
        context.IdempotencyKey = movementKey;
        var fileBefore = await Scalar($"SELECT to_jsonb(a)::text FROM attachments a WHERE id='{file}';");
        var previewBefore = await Scalar($"SELECT to_jsonb(p)::text FROM attachment_preview_publications p WHERE id='{previewId}';");
        var coverAdmission = provider.GetRequiredService<CardCoverAdmissionService>();
        var coverRead = new CardCoverReadService(coverAdmission, new PrivateAttachmentDownloadPreparer(objects));
        var oldCoverGrant = (await coverAdmission.AdmitAsync(activeCard, recipient, ct)).Value;
        Require(oldCoverGrant is not null, "Source cover admission was unavailable.");
        var downloadAdmission = provider.GetRequiredService<AttachmentDownloadAdmissionService>();
        var download = new AttachmentDownloadService(downloadAdmission, new PrivateAttachmentDownloadPreparer(objects));
        var oldFileGrant = (await downloadAdmission.AdmitAsync(activeCard, file, recipient, ct)).Value;
        Require(oldFileGrant is not null, "Source file admission fixture was unavailable.");
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
        var auditsBefore = int.Parse(await Scalar($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}';"));
        var eventsBefore = int.Parse(await Scalar($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';"));
        var readyJobsBefore = int.Parse(await Scalar($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='WORK_EVENT_READY';"));
        var activeVersionBefore = long.Parse(await Scalar($"SELECT version FROM cards WHERE id='{activeCard}';"));
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
        Require(await Scalar($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}';") == (auditsBefore + 4 + changedReminders).ToString(), "Complete List/Card/Reminder audit count differs.");
        Require(await Scalar($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == (eventsBefore + 8 + changedReminders).ToString(), "Both Board streams omitted List/Card/Reminder events.");
        Require(await Scalar($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}' AND job_type='WORK_EVENT_READY';") == (readyJobsBefore + 8 + changedReminders).ToString(), "Movement omitted delivery jobs.");
        Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}';") == (eligible ? "1" : "0"), "Watch deduplication or destination admission failed.");
        Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}' AND recipient_id IN ('{actor}','{member}');") == "0", "Actor/source-only Board watch received a move notice.");
        if (eligible)
        {
            Require(await Scalar($"SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='{tenant}' AND recipient_id='{recipient}' AND board_id='{destination}' AND card_id='{activeCard}' AND notification_type='CARD_MOVED';") == "1", "Eligible notice did not bind the active destination Card.");
            Require(await Scalar($"SELECT to_jsonb(r)::text FROM card_reminders r WHERE tenant_id='{tenant}' AND card_id='{activeCard}';") == activeReminderBefore, "Eligible overdue Reminder choice changed.");
        }
        Require(await Scalar($"SELECT count(*) FROM card_reminders WHERE tenant_id='{tenant}' AND status='SUSPENDED' AND generation=2 AND version=2 AND trigger_at IS NULL;") == changedReminders.ToString(), "Hidden/ineligible Reminder remained scheduled.");
        Require(await Scalar($"SELECT jsonb_agg(to_jsonb(j) ORDER BY id)::text FROM background_jobs j WHERE tenant_id='{tenant}' AND job_type='CARD_REMINDER';") == queuedBefore, "Move replaced or altered original overdue jobs.");
        Require(await Scalar($"SELECT count(*) FROM work_command_replays WHERE tenant_id='{tenant}' AND key_id='{movementKey}' AND result_json IS NOT NULL;") == "1", "Original success receipt was not retained once.");
        Require(await Scalar($"SELECT count(*) FROM cards WHERE tenant_id='{tenant}' AND board_id='{destination}' AND ((id='{activeCard}' AND version={activeVersionBefore + 1}) OR (id<>'{activeCard}' AND version=2));") == "3", "Movement lost a contained lifecycle.");
        var committed = await Scalar(GraphHash());
        Require(await movement.MoveAsync(list, actor, source, destination, 1, null, "list-app-test", ct) == success, "Original receipt replay differs.");
        Require(await Scalar(GraphHash()) == committed, "Replay reapplied movement or publication.");
        Require((await movement.MoveAsync(list, actor, source, destination, 1, Guid.NewGuid(), "list-app-test", ct)).ErrorCode == "idempotency_key_reused", "Changed replay body was admitted.");
        Require(await Scalar(GraphHash()) == committed, "Changed replay mutated protected state.");
        Require(await Scalar($"SELECT to_jsonb(a)::text FROM attachments a WHERE id='{file}';") == fileBefore,
            "List movement changed stable File identity, integrity or lifecycle metadata.");
        Require(!(await downloadAdmission.RevalidateAsync(oldFileGrant!, recipient, ct)).Succeeded,
            "File admission from the former source Board survived movement.");
        var beforeRead = objects.Reads;
        Require(!(await download.PrepareAsync(activeCard, file, member, ct)).Succeeded && objects.Reads == beforeRead,
            "Source-only member reached moved File provider bytes.");
        var content = await download.PrepareAsync(activeCard, file, recipient, ct);
        Require(content.Succeeded == eligible, "File delivery ignored current private-destination membership.");
        if (content.Value is { } admittedFile)
        {
            await using (admittedFile)
            {
                using var copied = new MemoryStream(); await admittedFile.Bytes.CopyToAsync(copied, ct);
                Require(copied.ToArray().SequenceEqual(bytes), "Moved File changed its integrity-bound bytes.");
            }
        }
        else Require(objects.Reads == beforeRead, "Destination-ineligible File request reached provider bytes.");
        if (eligible)
        {
            objects.AfterRead = () => Sql($"UPDATE board_members SET status='REMOVED',updated_at=now() WHERE tenant_id='{tenant}' AND board_id='{destination}' AND user_id='{recipient}';");
            try
            {
                Require(!(await download.PrepareAsync(activeCard, file, recipient, ct)).Succeeded && objects.Reads == beforeRead + 2,
                    "Destination permission loss during File read retained delivery.");
            }
            finally
            {
                objects.AfterRead = null;
                await Sql($"UPDATE board_members SET status='ACTIVE',updated_at=now() WHERE tenant_id='{tenant}' AND board_id='{destination}' AND user_id='{recipient}';");
            }
        }
        Require(await Scalar($"SELECT to_jsonb(p)::text FROM attachment_preview_publications p WHERE id='{previewId}';") == previewBefore,
            "List movement rewrote immutable preview publication history.");
        Require(await Scalar($"SELECT cover_attachment_id::text FROM cards WHERE id='{activeCard}';") == file.ToString(),
            "List movement lost the stable selected cover.");
        Require(!(await coverAdmission.RevalidateAsync(oldCoverGrant!, recipient, ct)).Succeeded,
            "Former source cover admission survived movement.");
        var coverReads = objects.Reads;
        Require(!(await coverRead.PrepareAsync(activeCard, member, ct)).Succeeded
            && !(await coverRead.PrepareAsync(activeCard, null, ct)).Succeeded && objects.Reads == coverReads,
            "Private moved cover widened source-only/anonymous access before provider I/O.");
        var coverContent = await coverRead.PrepareAsync(activeCard, recipient, ct);
        Require(coverContent.Succeeded == eligible, "Moved cover ignored current destination membership.");
        if (coverContent.Value is { } admittedCover)
        {
            await using (admittedCover)
            {
                using var copied = new MemoryStream(); await admittedCover.Bytes.CopyToAsync(copied, ct);
                Require(copied.ToArray().SequenceEqual(bytes) && objects.LastRead == AttachmentObjectReference.ForPreview(tenant, previewId),
                    "Moved cover did not use its integrity-bound preview namespace.");
            }
        }
        else Require(objects.Reads == coverReads, "Ineligible cover request reached private preview bytes.");
        if (eligible)
        {
            objects.AfterRead = () => Sql($"UPDATE board_members SET status='REMOVED',updated_at=now() WHERE tenant_id='{tenant}' AND board_id='{destination}' AND user_id='{recipient}';");
            try
            {
                Require(!(await coverRead.PrepareAsync(activeCard, recipient, ct)).Succeeded && objects.Reads == coverReads + 2,
                    "Late destination permission loss retained cover delivery.");
            }
            finally
            {
                objects.AfterRead = null;
                await Sql($"UPDATE board_members SET status='ACTIVE',updated_at=now() WHERE tenant_id='{tenant}' AND board_id='{destination}' AND user_id='{recipient}';");
            }
        }
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
        Console.WriteLine("Moved original File: stable metadata/bytes, former admission withdrawn, current destination rights, pre-I/O denial and late permission loss passed; measured bytes and scanner verdict are fixtures.");
        Console.WriteLine("Moved cover: actual Worker preview publication and cover command, stable selection/history, preview namespace/integrity, old admission withdrawal and current/late destination permission checks passed; encoding/provider are fixtures.");
    }
}
