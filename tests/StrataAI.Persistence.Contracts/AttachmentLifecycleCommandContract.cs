using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

// Uses the real production transaction/adapters and explicitly synthetic actor
// admission. HTTP/session withdrawal has its separate API contracts.
internal static class AttachmentLifecycleCommandContract
{
    private sealed class Context : IWorkCommandContext { public Guid? IdempotencyKey { get; set; } = Guid.NewGuid(); }
    private sealed class Actor : ICommandActorAuthorization
    {
        public bool Allowed = true;
        public Task<bool> VerifyAsync(Guid actor, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Allowed); }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddMinutes(1); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid actor,
        Func<Task<Guid>> createCard, CancellationToken ct)
    {
        var card = await createCard(); var file = Guid.NewGuid(); var context = new Context(); var authorization = new Actor();
        var work = provider.GetRequiredService<IWorkManagementStore>(); var metadata = provider.GetRequiredService<IAttachmentMetadataStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var service = new AttachmentLifecycleService(work, metadata, provider.GetRequiredService<IOrganizationStore>(),
            provider.GetRequiredService<IWorkBoardAuthorization>(), unit, context, authorization, new Clock(), provider.GetRequiredService<IWorkEventStore>(), provider.GetRequiredService<ICardAttachmentCoverStore>());
        var hint = (await work.FindCardAsync(card, ct))!;
        await unit.ExecuteReadAsync(tenant, actor, "card_not_found", () => work.AcquireCommandScopeAsync(tenant, actor, hint.BoardId, ct), async () =>
        {
            var value = await metadata.CreateUrlAttachmentAsync(file, tenant, card, actor, "Lifecycle command", "https://example.test/private", DateTimeOffset.UtcNow, ct);
            return WorkOperation<AttachmentMetadata>.Success(value);
        }, ct);
        async Task<string> Snapshot()
        {
            await using var query = new NpgsqlCommand("""
                SELECT jsonb_build_object('card',c.version,'title',c.title,'description',c.description,'rank',c.rank,
                  'file',a.version,'state',a.lifecycle_state,'archive',a.archived_at,'deleted',a.deleted_at,'actor',a.deleted_by,
                  'audit',(SELECT count(*) FROM audit_events WHERE tenant_id=@tenant),
                  'events',(SELECT count(*) FROM work_events WHERE tenant_id=@tenant),
                  'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant),
                  'sequence',COALESCE((SELECT last_sequence FROM work_event_streams WHERE tenant_id=@tenant AND board_id=c.board_id),0),
                  'receipt',(SELECT count(*) FROM work_command_replays WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key))::text
                FROM cards c JOIN attachments a ON a.tenant_id=c.tenant_id AND a.card_id=c.id
                WHERE c.tenant_id=@tenant AND c.id=@card AND a.id=@file;
                """, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("file", file);
            query.Parameters.AddWithValue("actor", actor); query.Parameters.AddWithValue("key", context.IdempotencyKey!.Value);
            return (string)(await query.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Lifecycle snapshot unavailable."));
        }
        var initial = await Snapshot(); var input = new AttachmentLifecycleInput(1, 1);
        await using (var inject = new NpgsqlCommand($"""
            CREATE FUNCTION public.__contract_lifecycle_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.tenant_id='{tenant:D}'::uuid AND NEW.event_type='ATTACHMENT_ARCHIVED' THEN
              RAISE EXCEPTION 'Injected lifecycle audit failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER __contract_lifecycle_reject BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION public.__contract_lifecycle_reject();
            """, admin)) { await inject.ExecuteNonQueryAsync(ct); }
        try
        {
            Require((await service.ArchiveAsync(card, file, actor, input, "lifecycle-audit-failure", ct)).ErrorCode == "work_storage_unavailable", "Late lifecycle audit failure was admitted.");
            Require(await Snapshot() == initial, "Audit refusal partially committed lifecycle/Card/receipt effects.");
        }
        finally
        {
            await using var remove = new NpgsqlCommand("DROP TRIGGER __contract_lifecycle_reject ON audit_events; DROP FUNCTION public.__contract_lifecycle_reject();", admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        authorization.Allowed = false;
        Require((await service.ArchiveAsync(card, file, actor, input, "lifecycle-actor-refused", ct)).ErrorCode == "session_unavailable", "Final lifecycle actor refusal was ignored.");
        Require(await Snapshot() == initial, "Final actor refusal partially committed lifecycle/Card/audit/event/job/sequence/retry effects.");
        authorization.Allowed = true;
        var archived = await service.ArchiveAsync(card, file, actor, input, "lifecycle-first", ct);
        Require(archived.Succeeded && archived.Value is { Changed: true, CardVersion: 2, Attachment.Version: 2, Attachment.LifecycleState: AttachmentLifecycleState.Archived }, "Archive command did not persist canonical revisions.");
        var archiveSnapshot = await Snapshot();
        using (var before = JsonDocument.Parse(initial))
        using (var after = JsonDocument.Parse(archiveSnapshot))
        {
            foreach (var name in new[] { "audit", "events", "jobs", "sequence", "receipt" })
                Require(after.RootElement.GetProperty(name).GetInt64() == before.RootElement.GetProperty(name).GetInt64() + 1, "Archive command effect was not exactly once: " + name);
            foreach (var name in new[] { "title", "description", "rank" })
                Require(after.RootElement.GetProperty(name).GetRawText() == before.RootElement.GetProperty(name).GetRawText(), "Archive changed another Card field.");
        }
        var replay = await service.ArchiveAsync(card, file, actor, input, "lifecycle-replay", ct);
        Require(replay.Succeeded && JsonSerializer.Serialize(replay) == JsonSerializer.Serialize(archived) && await Snapshot() == archiveSnapshot,
            "Archive replay duplicated effects or changed its canonical receipt.");
        var page = await service.ListArchivedAsync(card, actor, null, ct);
        Require(page.Succeeded && page.Value is { CanRestore: true, CanDelete: true } && page.Value.Items.Single() == archived.Value!.Attachment,
            "Current authorized archive page did not disclose its retained metadata/capabilities.");
        context.IdempotencyKey = Guid.NewGuid();
        var beforeRestore = await Snapshot();
        Require((await service.RestoreAsync(card, file, actor, input, "lifecycle-stale", ct)).ErrorCode == "version_conflict" && await Snapshot() == beforeRestore,
            "Stale lifecycle revisions changed protected state.");
        var restored = await service.RestoreAsync(card, file, actor, new(2, 2), "lifecycle-restored", ct);
        Require(restored.Succeeded && restored.Value is { CardVersion: 3, Attachment.Version: 3, Attachment.LifecycleState: AttachmentLifecycleState.Active }
            && restored.Value.Attachment.ArchivedAt == archived.Value!.Attachment.ArchivedAt, "Restoration lost persisted history/revisions.");
        context.IdempotencyKey = Guid.NewGuid();
        Require((await service.ArchiveAsync(card, file, actor, new(3, 3), "lifecycle-rearchive", ct)).Succeeded, "Rearchive failed.");
        context.IdempotencyKey = Guid.NewGuid(); var beforeDelete = await Snapshot();
        Require((await service.DeleteAsync(card, file, actor, new(false, 4, 4), "lifecycle-no-consent", ct)).ErrorCode == "delete_confirmation_required"
            && await Snapshot() == beforeDelete, "Missing deletion consent changed state.");
        var deleted = await service.DeleteAsync(card, file, actor, new(true, 4, 4), "lifecycle-deleted", ct);
        Require(deleted.Succeeded && deleted.Value is { CardVersion: 5, Attachment.Version: 5, Attachment.LifecycleState: AttachmentLifecycleState.Deleted }
            && deleted.Value.Attachment.DeletedBy == actor && deleted.Value.Attachment.DeletedAt == deleted.Value.Attachment.UpdatedAt,
            "Permanent deletion did not persist actor/tombstone/revisions.");
        var deletedSnapshot = await Snapshot();
        Require(JsonSerializer.Serialize(await service.DeleteAsync(card, file, actor, new(true, 4, 4), "lifecycle-delete-replay", ct)) == JsonSerializer.Serialize(deleted)
            && await Snapshot() == deletedSnapshot, "Deletion replay duplicated effects.");
        context.IdempotencyKey = Guid.NewGuid();
        Require((await service.RestoreAsync(card, file, actor, new(5, 5), "lifecycle-deleted-restore", ct)).ErrorCode == "attachment_not_found",
            "Deleted metadata became restorable through the command layer.");
        Require((await service.ListArchivedAsync(card, actor, null, ct)).Value?.Items.Count == 0, "Archive page disclosed deleted metadata.");
        await using (var query = new NpgsqlCommand("""
            SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND entity_id=@card
              AND event_type IN ('ATTACHMENT_ARCHIVED','ATTACHMENT_RESTORED','ATTACHMENT_DELETED') AND entity_type='Card';
            """, admin))
        {
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card);
            Require(await query.ExecuteScalarAsync(ct) is 4L, "Lifecycle stream effects were missing or duplicated.");
        }
        Console.WriteLine("Restricted Application attachment lifecycle: current scope, Card/File CAS, retained history, explicit deletion consent, canonical replay, audit/final actor complete rollback including stream/jobs/sequence/receipt and content-free events passed.");
    }
}
