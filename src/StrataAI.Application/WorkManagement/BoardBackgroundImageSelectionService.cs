using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed class BoardBackgroundImageSelectionService(IWorkManagementStore work, IOrganizationStore organizations,
    IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions, IWorkCommandContext context,
    ICommandActorAuthorization actors, AttachmentPreviewReadService previews, AttachmentDownloadAdmissionService sourceAdmission,
    IClock clock, IWorkEventStore events)
{
    public async Task<WorkOperation<BoardRecord>> SelectAsync(Guid boardId, Guid actor, SelectBoardBackgroundImageInput input,
        string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindBoardAsync(boardId, ct);
        if (hint is null) return WorkOperation<BoardRecord>.Failure("board_not_found");
        var preflight = await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "board_not_found",
            () => Admit(hint, actor, ct), () => Task.FromResult(WorkOperation<bool>.Success(true)), ct);
        if (!preflight.Succeeded) return WorkOperation<BoardRecord>.Failure(preflight.ErrorCode ?? "board_not_found");
        // Provider verification runs outside the command transaction. A failed
        // source read may still recover a committed, independently owned image.
        var sourceHint = input.CardId == Guid.Empty ? null : await work.FindCardAsync(input.CardId, ct);
        var prepared = sourceHint?.BoardId == boardId && sourceHint.OrganizationId == hint.OrganizationId
            && input.AttachmentId != Guid.Empty && input.AttachmentVersion > 0
            ? await previews.PrepareAsync(input.CardId, input.AttachmentId, actor, ct, input.AttachmentVersion)
            : WorkOperation<AttachmentPreviewContent>.Failure("invalid_background");
        await using var content = prepared.Value;
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "BOARD_BACKGROUND_IMAGE_SELECTED", boardId, input, "board_not_found"),
            async receipt =>
            {
                if (!await Admit(hint, actor, ct)) return false;
                if (receipt is null) return true;
                return receipt.Id == boardId && receipt.OrganizationId == hint.OrganizationId && receipt.BackgroundType == "IMAGE"
                    && input.BoardVersion is > 0 and < long.MaxValue && receipt.Version == input.BoardVersion + 1
                    && Guid.TryParseExact(receipt.BackgroundValue, "D", out var id)
                    && await work.FindBoardBackgroundImageAsync(hint.OrganizationId, boardId, id, ct) is { CreatedBy: var by } && by == actor;
            }, async () =>
            {
                if (input.BoardVersion is <= 0 or long.MaxValue || input.AttachmentVersion <= 0
                    || input.CardId == Guid.Empty || input.AttachmentId == Guid.Empty)
                    return WorkOperation<BoardRecord>.Failure("invalid_background");
                var current = await work.FindBoardAsync(boardId, ct);
                if (current is null || current.Version != input.BoardVersion) return WorkOperation<BoardRecord>.Failure("version_conflict");
                if (current.Visibility == BoardVisibility.Public && !input.PublicVisibilityConfirmed)
                    return WorkOperation<BoardRecord>.Failure("background_public_confirmation_required");
                if (!prepared.Succeeded || content is null)
                    return WorkOperation<BoardRecord>.Failure(prepared.ErrorCode == "work_storage_unavailable" ? "work_storage_unavailable" : "invalid_background");
                var source = content.Admission.Source;
                if (source.Card.BoardId != boardId || source.Card.OrganizationId != hint.OrganizationId
                    || source.Card.Id != input.CardId || source.File.Metadata.Id != input.AttachmentId
                    || source.File.Metadata.Version != input.AttachmentVersion
                    || !(await sourceAdmission.RevalidatePreviewAsync(content.Admission, actor, ct)).Succeeded)
                    return WorkOperation<BoardRecord>.Failure("invalid_background");
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (now < current.UpdatedAt) return WorkOperation<BoardRecord>.Failure("version_conflict");
                var owned = await work.CreateBoardBackgroundImageAsync(new(Guid.NewGuid(), hint.OrganizationId, boardId,
                    actor, now, content.Admission.Preview), ct);
                var updated = await work.UpdateBoardAsync(boardId, current.Name, current.Description, "IMAGE", owned.Id.ToString("D"), current.Version, now, ct);
                if (updated is null) return WorkOperation<BoardRecord>.Failure("version_conflict");
                await work.AppendAuditAsync(hint.OrganizationId, actor, "BOARD_UPDATED", "Board", boardId, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, boardId, actor, "BOARD_UPDATED", "Board", boardId,
                    updated.Version, correlationId, now), ct);
                if (!await Admit(hint, actor, ct) || !await actors.VerifyAsync(actor, ct))
                    return WorkOperation<BoardRecord>.Failure("board_not_found");
                return WorkOperation<BoardRecord>.Success(updated);
            }, ct);
    }

    private async Task<bool> Admit(BoardRecord hint, Guid actor, CancellationToken ct)
    {
        if (!await work.AcquireCommandScopeAsync(hint.OrganizationId, actor, hint.Id, ct)) return false;
        var scope = (await boards.GetSyncScopeAsync(hint.Id, actor, ct)).Value;
        return scope is { Access.CanEdit: true, Board.LifecycleState: BoardLifecycleState.Active }
            && scope.Board.OrganizationId == hint.OrganizationId
            && await organizations.FindOrganizationAsync(hint.OrganizationId, ct) is { Status: OrganizationStatus.Active }
            && await organizations.FindMembershipAsync(hint.OrganizationId, actor, ct) is { Active: true }
            && await actors.VerifyAsync(actor, ct);
    }
}
