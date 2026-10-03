using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

// Genuine restricted PostgreSQL adapters/publication and owning transaction;
// only session proof is explicitly synthetic and controllable for late fences.
internal static class CardCoverCommandContract
{
    private sealed class Context : IWorkCommandContext { public Guid? IdempotencyKey { get; set; } = Guid.NewGuid(); }
    private sealed class Actor : ICommandActorAuthorization
    {
        public bool Allowed = true;
        public Task<bool> VerifyAsync(Guid actor, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Allowed); }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddMinutes(5); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid card, Guid file, Guid actor, CancellationToken ct)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var context = new Context(); var authorization = new Actor(); var clock = new Clock();
        var work = provider.GetRequiredService<IWorkManagementStore>(); var metadata = provider.GetRequiredService<IAttachmentMetadataStore>();
        var covers = provider.GetRequiredService<ICardAttachmentCoverStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var organizations = provider.GetRequiredService<IOrganizationStore>(); var boards = provider.GetRequiredService<IWorkBoardAuthorization>();
        var events = provider.GetRequiredService<IWorkEventStore>();
        var service = new CardAttachmentCoverService(work, metadata, covers, organizations, boards, unit, context, authorization, clock, events);
        var lifecycle = new AttachmentLifecycleService(work, metadata, organizations, boards, unit, context, authorization, clock, events, covers);
        async Task<T> Scalar<T>(string sql)
        {
            await using var query = new NpgsqlCommand(sql, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("file", file);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        async Task<string> Snapshot() => await Scalar<string>("""
            SELECT jsonb_build_object('card',to_jsonb(c),'file',to_jsonb(a),
             'audit',(SELECT count(*) FROM audit_events WHERE tenant_id=@tenant),
             'events',(SELECT count(*) FROM work_events WHERE tenant_id=@tenant),
             'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant),
             'retry',(SELECT count(*) FROM work_command_replays WHERE tenant_id=@tenant),
             'sequence',(SELECT last_sequence FROM work_event_streams WHERE tenant_id=@tenant AND board_id=c.board_id))::text
            FROM cards c JOIN attachments a ON a.tenant_id=c.tenant_id AND a.card_id=c.id
            WHERE c.tenant_id=@tenant AND c.id=@card AND a.id=@file;
            """);
        var initial = (await service.ReadAsync(card, actor, ct)).Value;
        Require(initial is { AttachmentId: null, CanEdit: true }, "Cover command fixture cannot currently edit the owning Card.");
        var fileVersion = await Scalar<long>("SELECT version FROM attachments WHERE tenant_id=@tenant AND id=@file;");
        var input = new SetCardCoverInput(file, initial!.CardVersion, fileVersion);
        var baseline = await Snapshot();
        var stale = await service.SetAsync(card, actor, input with { AttachmentVersion = fileVersion + 1 }, "cover-stale", ct);
        Require(stale.ErrorCode == "version_conflict" && await Snapshot() == baseline, "Stale cover command retained effects.");
        context.IdempotencyKey = Guid.NewGuid();
        var trigger = $"ci_cover_audit_{card:N}";
        async Task Install() { await using var query = new NpgsqlCommand($"""
            CREATE FUNCTION public.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.event_type='CARD_COVER_CHANGED' AND NEW.entity_id='{card:D}'::uuid THEN
              RAISE EXCEPTION 'Injected cover audit failure' USING ERRCODE='23514';
             END IF; RETURN NEW; END $$;
            CREATE TRIGGER {trigger} BEFORE INSERT ON public.audit_events FOR EACH ROW EXECUTE FUNCTION public.{trigger}();
            """, admin); await query.ExecuteNonQueryAsync(ct); }
        async Task Remove() { await using var query = new NpgsqlCommand($"DROP TRIGGER {trigger} ON public.audit_events; DROP FUNCTION public.{trigger}();", admin); await query.ExecuteNonQueryAsync(ct); }
        await Install();
        try
        {
            try { await service.SetAsync(card, actor, input, "cover-audit-failure", ct); throw new InvalidOperationException("Cover audit failure committed."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation && error.MessageText == "Injected cover audit failure") { }
            Require(await Snapshot() == baseline, "Cover audit failure retained Card/selection/event/job/retry/sequence effects.");
        }
        finally { await Remove(); }
        authorization.Allowed = false;
        var withdrawn = await service.SetAsync(card, actor, input, "cover-late-session", ct);
        Require(withdrawn.ErrorCode == "session_unavailable" && await Snapshot() == baseline, "Late actor withdrawal retained cover effects.");
        authorization.Allowed = true;
        var key = context.IdempotencyKey;
        var selected = await service.SetAsync(card, actor, input, "cover-selection", ct);
        Require(selected.Value is { Changed: true } && selected.Value.CardVersion == initial.CardVersion + 1
            && selected.Value.AttachmentId == file && selected.Value.AttachmentVersion == fileVersion, "Cover selection lost source/Card revision binding.");
        Require(await Scalar<long>("SELECT version FROM attachments WHERE tenant_id=@tenant AND id=@file;") == fileVersion, "Cover selection advanced immutable source revision.");
        var committed = await Snapshot();
        Require((await service.SetAsync(card, actor, input, "cover-original-retry", ct)).Value == selected.Value && await Snapshot() == committed,
            "Cover original retry repeated effects or changed its acknowledgment.");
        Require((await service.SetAsync(card, actor, new(null, selected.Value!.CardVersion, null), "cover-key-reuse", ct)).ErrorCode == "idempotency_key_reused"
            && await Snapshot() == committed, "Cover retry key allowed another intent.");
        context.IdempotencyKey = Guid.NewGuid();
        var auditCount = await Scalar<long>("SELECT count(*) FROM audit_events WHERE tenant_id=@tenant;");
        var eventCount = await Scalar<long>("SELECT count(*) FROM work_events WHERE tenant_id=@tenant;");
        var noOp = await service.SetAsync(card, actor, input with { CardVersion = selected.Value.CardVersion }, "cover-no-op", ct);
        Require(noOp.Value is { Changed: false } && noOp.Value.CardVersion == selected.Value.CardVersion
            && await Scalar<long>("SELECT count(*) FROM audit_events WHERE tenant_id=@tenant;") == auditCount
            && await Scalar<long>("SELECT count(*) FROM work_events WHERE tenant_id=@tenant;") == eventCount, "Cover no-op advanced revisions/effects.");
        context.IdempotencyKey = Guid.NewGuid(); var archiveInput = new AttachmentLifecycleInput(selected.Value.CardVersion, fileVersion);
        baseline = await Snapshot(); await Install();
        try
        {
            try { await lifecycle.ArchiveAsync(card, file, actor, archiveInput, "cover-clear-audit-failure", ct); throw new InvalidOperationException("Cover clear audit failure committed."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation && error.MessageText == "Injected cover audit failure") { }
            Require(await Snapshot() == baseline, "Failed cover clearing retained attachment/Card/history/audit/event/job/retry/sequence effects.");
        }
        finally { await Remove(); }
        var archived = await lifecycle.ArchiveAsync(card, file, actor, archiveInput, "cover-archive-clear", ct);
        Require(archived.Value is { Changed: true } && archived.Value.CardVersion == selected.Value.CardVersion + 1
            && archived.Value.Attachment.Version == fileVersion + 1, "Archive cover clear consumed extra Card or File revisions.");
        var cleared = (await service.ReadAsync(card, actor, ct)).Value;
        Require(cleared is { AttachmentId: null }, "Archive retained the selected cover.");
        Require(await Scalar<long>("SELECT count(*) FROM audit_events WHERE tenant_id=@tenant;") == auditCount + 2
            && await Scalar<long>("SELECT count(*) FROM work_events WHERE tenant_id=@tenant;") == eventCount + 2, "Archive/cover clear lost its two canonical effects.");
        committed = await Snapshot();
        Require((await lifecycle.ArchiveAsync(card, file, actor, archiveInput, "cover-archive-retry", ct)).Value == archived.Value && await Snapshot() == committed,
            "Archive retry repeated cover clearing effects.");
        context.IdempotencyKey = key;
        Require((await service.SetAsync(card, actor, input, "withdrawn-cover-retry", ct)).ErrorCode == "card_not_found" && await Snapshot() == committed,
            "Old selection receipt remained admitted after selected source withdrawal.");
        context.IdempotencyKey = Guid.NewGuid();
        var restored = await lifecycle.RestoreAsync(card, file, actor, new(archived.Value!.CardVersion, fileVersion + 1), "cover-source-restore", ct);
        Require(restored.Succeeded && (await service.ReadAsync(card, actor, ct)).Value is { AttachmentId: null }, "Restoration silently reselected the old cover.");
        Console.WriteLine("Restricted cover commands: current authority, immutable published source, dual CAS, original replay/key reuse/no-op, late actor/audit full rollback, single-revision archive clearing, canonical effects and restoration without reselection passed.");
    }
}
