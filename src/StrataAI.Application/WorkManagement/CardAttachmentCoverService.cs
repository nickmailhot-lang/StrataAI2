using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed class CardAttachmentCoverService(IWorkManagementStore work, IAttachmentMetadataStore attachments,
    ICardAttachmentCoverStore covers, IOrganizationStore organizations, IWorkBoardAuthorization boards,
    IWorkManagementUnitOfWork transactions, IWorkCommandContext context, ICommandActorAuthorization actors,
    IClock clock, IWorkEventStore events)
{
    public async Task<WorkOperation<CardCoverView>> ReadAsync(Guid cardId, Guid actor, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardCoverView>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found", () => Admit(hint, actor, false, ct), async () =>
        {
            var current = await work.FindCardAsync(cardId, ct); var selected = await covers.FindSelectedAsync(hint.OrganizationId, cardId, ct);
            var file = selected is { } id ? await Source(hint, id, ct) : null;
            if (current is null || selected is not null && file is null || !await Admit(hint, actor, false, ct))
                return WorkOperation<CardCoverView>.Failure("card_not_found");
            var scope = (await boards.GetSyncScopeAsync(hint.BoardId, actor, ct)).Value;
            var list = await work.FindListAsync(hint.ListId, ct);
            var canEdit = scope?.Access.CanEdit == true && scope.Board.LifecycleState == BoardLifecycleState.Active
                && current.LifecycleState == WorkItemLifecycleState.Active && list?.LifecycleState == WorkItemLifecycleState.Active
                && (await organizations.FindOrganizationAsync(hint.OrganizationId, ct))?.Status == OrganizationStatus.Active;
            return WorkOperation<CardCoverView>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version,
                selected, file?.Metadata.Version, canEdit, scope?.Board.Visibility == BoardVisibility.Public));
        }, ct);
    }
    public async Task<WorkOperation<CardCoverChange>> SetAsync(Guid cardId, Guid actor, SetCardCoverInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardCoverChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "CARD_COVER_CHANGED", cardId, input, "card_not_found"), async receipt =>
            {
                if (!await Admit(hint, actor, true, ct)) return false;
                var selected = await covers.FindSelectedAsync(hint.OrganizationId, cardId, ct);
                if (receipt is null) return true;
                if (receipt.OrganizationId != hint.OrganizationId || receipt.BoardId != hint.BoardId || receipt.CardId != cardId || selected != receipt.AttachmentId) return false;
                return selected is null || await Source(hint, selected.Value, ct) is { } source && source.Metadata.Version >= receipt.AttachmentVersion;
            }, async () =>
            {
                if (input.CardVersion < 1 || input.AttachmentId == Guid.Empty || input.AttachmentId.HasValue != input.AttachmentVersion.HasValue
                    || input.AttachmentVersion is <= 0) return WorkOperation<CardCoverChange>.Failure("invalid_card_cover");
                var current = await work.FindCardAsync(cardId, ct);
                if (current is null || current.Version != input.CardVersion) return WorkOperation<CardCoverChange>.Failure("version_conflict");
                var selected = await covers.FindSelectedAsync(hint.OrganizationId, cardId, ct);
                var source = input.AttachmentId is { } id ? await Source(hint, id, ct) : null;
                if (input.AttachmentId is not null && source is null) return WorkOperation<CardCoverChange>.Failure("card_not_found");
                if (source is not null && source.Metadata.Version != input.AttachmentVersion) return WorkOperation<CardCoverChange>.Failure("version_conflict");
                if (!await Disclosure(hint, actor, input, ct)) return WorkOperation<CardCoverChange>.Failure("cover_public_confirmation_required");
                var changed = selected != input.AttachmentId; var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (changed && (current.Version == long.MaxValue || now < current.UpdatedAt)) return WorkOperation<CardCoverChange>.Failure("version_conflict");
                if (changed)
                {
                    current = await covers.SetAsync(hint.OrganizationId, hint.BoardId, cardId, input.AttachmentId,
                        input.AttachmentVersion, selected, current.Version, now, ct);
                    if (current is null) return WorkOperation<CardCoverChange>.Failure("version_conflict");
                    await work.AppendAuditAsync(hint.OrganizationId, actor, "CARD_COVER_CHANGED", "Card", cardId, correlationId, ct);
                    await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "CARD_COVER_CHANGED", "Card", cardId, current.Version, correlationId, now), ct);
                }
                if (!await Admit(hint, actor, true, ct) || !await Disclosure(hint, actor, input, ct)) return WorkOperation<CardCoverChange>.Failure("card_not_found");
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<CardCoverChange>.Failure("session_unavailable");
                return WorkOperation<CardCoverChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version,
                    input.AttachmentId, input.AttachmentVersion, changed));
            }, ct);
    }
    private Task<bool> Admit(CardRecord hint, Guid actor, bool editing, CancellationToken ct)
        => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, editing, ct);
    private async Task<bool> Disclosure(CardRecord hint, Guid actor, SetCardCoverInput input, CancellationToken ct)
        => input.AttachmentId is null || input.PublicVisibilityConfirmed
            || (await boards.GetSyncScopeAsync(hint.BoardId, actor, ct)).Value?.Board.Visibility != BoardVisibility.Public;
    private async Task<AttachmentFileRecord?> Source(CardRecord hint, Guid id, CancellationToken ct)
    {
        var file = await attachments.FindFileAttachmentAsync(hint.OrganizationId, hint.Id, id, ct);
        if (file is null || file.Metadata.Id != id || file.Metadata.OrganizationId != hint.OrganizationId || file.Metadata.CardId != hint.Id
            || file.Metadata.Kind != AttachmentKind.File || file.Metadata.LifecycleState != AttachmentLifecycleState.Active
            || file.Metadata.DeletedAt is not null || file.Metadata.ScanStatus != AttachmentScanStatus.Clean || file.Metadata.ScannedAt is null
            || file.Metadata.Version < 3 || file.Metadata.MimeType is not ("image/png" or "image/jpeg" or "image/webp")
            || file.Integrity.Reference.IsPreview || file.Integrity.Reference.OrganizationId != hint.OrganizationId || file.Integrity.Reference.AttachmentId != id
            || file.Integrity.SizeBytes != file.Metadata.SizeBytes) return null;
        var preview = await attachments.FindPublishedPreviewAsync(file, ct);
        return preview is not null && preview.Integrity.Reference.IsPreview && preview.Integrity.Reference.OrganizationId == hint.OrganizationId
            && preview.Integrity.Reference.AttachmentId != id && preview.Integrity.SizeBytes is >= 45 and <= 8_388_608
            && preview.Width is >= 1 and <= 1024 && preview.Height is >= 1 and <= 1024 ? file : null;
    }
}
