using System.Globalization;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record AttachmentLifecycleInput(long CardVersion, long Version);
public sealed record DeleteAttachmentInput(bool Confirmed, long CardVersion, long Version);
public sealed record AttachmentLifecycleChange(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    AttachmentMetadata Attachment, bool Changed);
public sealed record AttachmentArchivePage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    IReadOnlyList<AttachmentMetadata> Items, string? NextCursor, bool CanRestore, bool CanDelete);

public sealed class AttachmentLifecycleService(IWorkManagementStore work, IAttachmentMetadataStore attachments,
    IOrganizationStore organizations, IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions,
    IWorkCommandContext context, ICommandActorAuthorization actors, IClock clock, IWorkEventStore events)
{
    public Task<WorkOperation<AttachmentLifecycleChange>> ArchiveAsync(Guid card, Guid attachment, Guid actor,
        AttachmentLifecycleInput input, string correlationId, CancellationToken ct = default) =>
        ChangeAsync(card, attachment, actor, input, AttachmentLifecycleState.Archived, false, correlationId, ct);
    public Task<WorkOperation<AttachmentLifecycleChange>> RestoreAsync(Guid card, Guid attachment, Guid actor,
        AttachmentLifecycleInput input, string correlationId, CancellationToken ct = default) =>
        ChangeAsync(card, attachment, actor, input, AttachmentLifecycleState.Active, false, correlationId, ct);
    public Task<WorkOperation<AttachmentLifecycleChange>> DeleteAsync(Guid card, Guid attachment, Guid actor,
        DeleteAttachmentInput input, string correlationId, CancellationToken ct = default) =>
        ChangeAsync(card, attachment, actor, new(input.CardVersion, input.Version), AttachmentLifecycleState.Deleted, input.Confirmed, correlationId, ct);

    private async Task<WorkOperation<AttachmentLifecycleChange>> ChangeAsync(Guid cardId, Guid attachmentId, Guid actor,
        AttachmentLifecycleInput input, AttachmentLifecycleState next, bool confirmed, string correlationId, CancellationToken ct)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentLifecycleChange>.Failure("card_not_found");
        var deleting = next == AttachmentLifecycleState.Deleted;
        var action = next switch { AttachmentLifecycleState.Archived => "ATTACHMENT_ARCHIVED", AttachmentLifecycleState.Active => "ATTACHMENT_RESTORED", _ => "ATTACHMENT_DELETED" };
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, action, attachmentId, new { cardId, input, confirmed }, "attachment_not_found"),
            async receipt =>
            {
                if (!await Admit(hint, actor, deleting, ct)) return false;
                var current = await attachments.FindLifecycleAttachmentAsync(hint.OrganizationId, cardId, attachmentId, ct);
                // Default admission includes Active/Archived (or a Deleted command's tombstone).
                // A prior receipt is disclosed only while its current lifecycle still matches.
                if (current is null || !deleting && current.LifecycleState == AttachmentLifecycleState.Deleted) return false;
                return receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId
                    && receipt.CardId == cardId && receipt.Attachment.Id == attachmentId && receipt.Attachment.OrganizationId == hint.OrganizationId
                    && receipt.Attachment.CardId == cardId && receipt.Attachment.LifecycleState == next
                    && current.LifecycleState == next && current.Version >= receipt.Attachment.Version
                    && (!deleting || receipt.Attachment.DeletedBy is not null && current.DeletedBy == receipt.Attachment.DeletedBy);
            }, async () =>
            {
                if (deleting && !confirmed) return WorkOperation<AttachmentLifecycleChange>.Failure("delete_confirmation_required");
                if (input.CardVersion < 1 || input.Version < 1) return WorkOperation<AttachmentLifecycleChange>.Failure("invalid_attachment_version");
                var current = await work.FindCardAsync(cardId, ct);
                var child = await attachments.FindLifecycleAttachmentAsync(hint.OrganizationId, cardId, attachmentId, ct);
                if (current is null || child is null) return WorkOperation<AttachmentLifecycleChange>.Failure("attachment_not_found");
                if (current.Version != input.CardVersion || child.Version != input.Version) return WorkOperation<AttachmentLifecycleChange>.Failure("version_conflict");
                if (child.LifecycleState == next)
                {
                    if (!await Admit(hint, actor, deleting, ct)) return WorkOperation<AttachmentLifecycleChange>.Failure("attachment_not_found");
                    if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<AttachmentLifecycleChange>.Failure("session_unavailable");
                    return WorkOperation<AttachmentLifecycleChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, child, false));
                }
                if (child.LifecycleState == AttachmentLifecycleState.Deleted || deleting && child.LifecycleState != AttachmentLifecycleState.Archived)
                    return WorkOperation<AttachmentLifecycleChange>.Failure("invalid_lifecycle_transition");
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (now < current.UpdatedAt || now < child.UpdatedAt || child.Version == long.MaxValue || current.Version == long.MaxValue)
                    return WorkOperation<AttachmentLifecycleChange>.Failure("version_conflict");
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, current.Version, now, ct);
                if (updated is null) return WorkOperation<AttachmentLifecycleChange>.Failure("version_conflict");
                var changed = await attachments.ChangeAttachmentLifecycleAsync(hint.OrganizationId, cardId, attachmentId,
                    child.Version, child.LifecycleState, next, actor, now, ct);
                if (changed is null) return WorkOperation<AttachmentLifecycleChange>.Failure("version_conflict");
                await work.AppendAuditAsync(hint.OrganizationId, actor, action, "Attachment", attachmentId, correlationId, ct);
                // The stream invalidates the Card; private file/URL details stay out of the event.
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, action, "Card", cardId, updated.Version, correlationId, now), ct);
                if (!await Admit(hint, actor, deleting, ct)) return WorkOperation<AttachmentLifecycleChange>.Failure("attachment_not_found");
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<AttachmentLifecycleChange>.Failure("session_unavailable");
                return WorkOperation<AttachmentLifecycleChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, changed, true));
            }, ct);
    }
    public async Task<WorkOperation<AttachmentArchivePage>> ListArchivedAsync(Guid cardId, Guid actor, string? after, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentArchivePage>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, false, ct), async () =>
            {
                DateTimeOffset? created = null; Guid? id = null;
                if (after is not null)
                {
                    var parts = after.Length <= 140 ? after.Split('/') : [];
                    if (parts.Length != 4 || parts[0] != "archive" || !Guid.TryParseExact(parts[1], "D", out var parent) || parent != cardId
                        || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                        || ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks || ticks % 10 != 0
                        || !Guid.TryParseExact(parts[3], "D", out var parsed) || parsed == Guid.Empty)
                        return WorkOperation<AttachmentArchivePage>.Failure("invalid_attachment_cursor");
                    created = new(ticks, TimeSpan.Zero); id = parsed;
                }
                var current = await work.FindCardAsync(cardId, ct);
                var rows = await attachments.ListArchivedAttachmentsAsync(hint.OrganizationId, cardId, created, id, ct);
                if (current is null || !await AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, false, ct))
                    return WorkOperation<AttachmentArchivePage>.Failure("card_not_found");
                var items = rows.Take(50).ToArray();
                var cursor = rows.Count > 50 ? $"archive/{cardId:D}/{items[^1].CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}/{items[^1].Id:D}" : null;
                // A read keeps SHARE locks. Do not upgrade to a write gate to compute capabilities.
                var scope = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
                var list = await work.FindListAsync(current.ListId, ct);
                var active = scope.Value?.Board.LifecycleState == BoardLifecycleState.Active
                    && current.LifecycleState == WorkItemLifecycleState.Active && list?.LifecycleState == WorkItemLifecycleState.Active
                    && (await organizations.FindOrganizationAsync(hint.OrganizationId, ct))?.Status == OrganizationStatus.Active;
                return WorkOperation<AttachmentArchivePage>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, items, cursor,
                    active && scope.Value?.Access.CanEdit == true, active && scope.Value?.Access.CanAdminister == true));
            }, ct);
    }
    private async Task<bool> Admit(CardRecord hint, Guid actor, bool deleting, CancellationToken ct)
    {
        if (!await AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct)) return false;
        return !deleting || (await boards.GetSyncScopeAsync(hint.BoardId, actor, ct)).Value?.Access.CanAdminister == true;
    }
}
