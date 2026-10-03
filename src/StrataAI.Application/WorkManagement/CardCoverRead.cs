using System.Text.Json.Serialization;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// Server-owned current selection snapshot. Never a serialized bearer grant.
public sealed class CardCoverAdmission
{
    internal CardCoverAdmission(Guid? actor, CardRecord card, BoardRecord board, AttachmentFileRecord file,
        AttachmentPublishedPreview preview, DateTimeOffset at)
    { ActorId = actor; Card = card; Board = board; File = file; Preview = preview; AdmittedAt = at; ExpiresAt = at.AddMinutes(1); }
    [JsonIgnore] public Guid? ActorId { get; }
    [JsonIgnore] public CardRecord Card { get; }
    [JsonIgnore] public BoardRecord Board { get; }
    [JsonIgnore] public AttachmentFileRecord File { get; }
    [JsonIgnore] public AttachmentPublishedPreview Preview { get; }
    [JsonIgnore] public DateTimeOffset AdmittedAt { get; }
    [JsonIgnore] public DateTimeOffset ExpiresAt { get; }
}

public sealed class CardCoverAdmissionService(IWorkManagementStore work, IAttachmentMetadataStore attachments,
    ICardAttachmentCoverStore covers, IOrganizationStore organizations, IWorkBoardAuthorization boards,
    IWorkManagementUnitOfWork transactions, IClock clock)
{
    public async Task<WorkOperation<CardCoverAdmission>> AdmitAsync(Guid cardId, Guid? actor, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardCoverAdmission>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found", () => Scope(hint, actor, ct), async () =>
        {
            var current = await work.FindCardAsync(cardId, ct);
            var board = (await boards.GetSyncScopeAsync(hint.BoardId, actor, ct)).Value?.Board;
            var selected = await covers.FindSelectedAsync(hint.OrganizationId, cardId, ct);
            if (current is null || board is null || selected is null) return WorkOperation<CardCoverAdmission>.Failure("card_not_found");
            var file = await attachments.FindFileAttachmentAsync(hint.OrganizationId, cardId, selected.Value, ct);
            if (!Source(file, current, selected.Value)) return WorkOperation<CardCoverAdmission>.Failure("card_not_found");
            var preview = await attachments.FindPublishedPreviewAsync(file!, ct);
            return Published(preview, file!)
                ? WorkOperation<CardCoverAdmission>.Success(new(actor, current, board, file!, preview!, clock.UtcNow))
                : WorkOperation<CardCoverAdmission>.Failure("card_not_found");
        }, ct);
    }

    public async Task<WorkOperation<bool>> RevalidateAsync(CardCoverAdmission admitted, Guid? actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        bool Timely() => actor == admitted.ActorId && clock.UtcNow >= admitted.AdmittedAt && clock.UtcNow < admitted.ExpiresAt;
        if (!Timely()) return WorkOperation<bool>.Failure("card_not_found");
        var hint = admitted.Card;
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            async () => Timely() && await Scope(hint, actor, ct), async () =>
            {
                var current = await work.FindCardAsync(hint.Id, ct);
                var board = (await boards.GetSyncScopeAsync(hint.BoardId, actor, ct)).Value?.Board;
                var selected = await covers.FindSelectedAsync(hint.OrganizationId, hint.Id, ct);
                if (current != hint || board != admitted.Board || selected != admitted.File.Metadata.Id)
                    return WorkOperation<bool>.Failure("card_not_found");
                var file = await attachments.FindFileAttachmentAsync(hint.OrganizationId, hint.Id, selected.Value, ct);
                if (file != admitted.File || !Source(file, hint, selected.Value)) return WorkOperation<bool>.Failure("card_not_found");
                var preview = await attachments.FindPublishedPreviewAsync(file!, ct);
                return Timely() && preview == admitted.Preview && Published(preview, file!)
                    ? WorkOperation<bool>.Success(true) : WorkOperation<bool>.Failure("card_not_found");
            }, ct);
    }

    private async Task<bool> Scope(CardRecord hint, Guid? actor, CancellationToken ct)
    {
        var initial = (await boards.GetSyncScopeAsync(hint.BoardId, actor, ct)).Value;
        if (initial is null || !initial.Access.CanView || initial.Board.OrganizationId != hint.OrganizationId) return false;
        if (initial.Board.Visibility == BoardVisibility.Public)
        {
            if (!await covers.AcquirePublicReadScopeAsync(hint.OrganizationId, hint.BoardId, hint.ListId, hint.Id, ct)) return false;
        }
        else if (actor is not { } member || initial.Board.Visibility is not (BoardVisibility.Private or BoardVisibility.Organization)
            || !await AttachmentAdmission.CheckAsync(work, organizations, boards, hint, member, false, ct)) return false;
        var scope = (await boards.GetSyncScopeAsync(hint.BoardId, actor, ct)).Value;
        var card = await work.FindCardAsync(hint.Id, ct); var list = await work.FindListAsync(hint.ListId, ct);
        return scope?.Access.CanView == true && scope.Board.OrganizationId == hint.OrganizationId
            && scope.Board.LifecycleState == BoardLifecycleState.Active
            && (actor is not null || scope.Board.Visibility == BoardVisibility.Public)
            && card is { LifecycleState: WorkItemLifecycleState.Active } && card.OrganizationId == hint.OrganizationId
            && card.BoardId == hint.BoardId && card.ListId == hint.ListId
            && list is { LifecycleState: WorkItemLifecycleState.Active } && list.OrganizationId == hint.OrganizationId && list.BoardId == hint.BoardId
            && (await organizations.FindOrganizationAsync(hint.OrganizationId, ct))?.Status == OrganizationStatus.Active;
    }

    private static bool Source(AttachmentFileRecord? file, CardRecord card, Guid id) => file is not null
        && file.Metadata.Id == id && file.Metadata.OrganizationId == card.OrganizationId && file.Metadata.CardId == card.Id
        && file.Metadata.Kind == AttachmentKind.File && file.Metadata.LifecycleState == AttachmentLifecycleState.Active
        && file.Metadata.DeletedAt is null && file.Metadata.Url is null && file.Metadata.ScanStatus == AttachmentScanStatus.Clean
        && file.Metadata.Version >= 3 && file.Metadata.ScannedAt is not null && file.Metadata.ScannedAt >= file.Metadata.CreatedAt
        && file.Metadata.ScannedAt <= file.Metadata.UpdatedAt && file.Metadata.SizeBytes is > 0 and <= 1073741824
        && file.Metadata.SizeBytes == file.Integrity.SizeBytes && file.Metadata.MimeType is "image/png" or "image/jpeg" or "image/webp"
        && !file.Integrity.Reference.IsPreview && file.Integrity.Reference.OrganizationId == card.OrganizationId
        && file.Integrity.Reference.AttachmentId == id;
    private static bool Published(AttachmentPublishedPreview? preview, AttachmentFileRecord file) => preview is not null
        && preview.Integrity.Reference.IsPreview && preview.Integrity.Reference.OrganizationId == file.Metadata.OrganizationId
        && preview.Integrity.Reference.AttachmentId != file.Metadata.Id && preview.Integrity.SizeBytes is >= 45 and <= 8388608
        && preview.Width is >= 1 and <= 1024 && preview.Height is >= 1 and <= 1024;
}

public sealed class CardCoverContent : IAsyncDisposable
{
    internal CardCoverContent(Stream bytes, CardCoverAdmission admission) { Bytes = bytes; Admission = admission; }
    [JsonIgnore] public Stream Bytes { get; }
    [JsonIgnore] public CardCoverAdmission Admission { get; }
    [JsonIgnore] public long SizeBytes => Admission.Preview.Integrity.SizeBytes;
    public ValueTask DisposeAsync() => Bytes.DisposeAsync();
}

public sealed class CardCoverReadService(CardCoverAdmissionService admission, IAttachmentDownloadPreparer preparer)
{
    public async Task<WorkOperation<CardCoverContent>> PrepareAsync(Guid card, Guid? actor, CancellationToken ct = default, long? cardVersion = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(55));
        Stream? prepared = null;
        try
        {
            var admitted = await admission.AdmitAsync(card, actor, deadline.Token);
            if (!admitted.Succeeded || admitted.Value is null) return WorkOperation<CardCoverContent>.Failure(admitted.ErrorCode ?? "card_not_found");
            if (cardVersion is { } version && version != admitted.Value.Card.Version) return WorkOperation<CardCoverContent>.Failure("card_not_found");
            prepared = await preparer.PrepareAsync(admitted.Value.Preview.Integrity, deadline.Token);
            if (prepared is null) return WorkOperation<CardCoverContent>.Failure("card_not_found");
            var current = await admission.RevalidateAsync(admitted.Value, actor, deadline.Token);
            if (!current.Succeeded) return WorkOperation<CardCoverContent>.Failure(current.ErrorCode ?? "card_not_found");
            deadline.Token.ThrowIfCancellationRequested();
            var result = new CardCoverContent(prepared, admitted.Value); prepared = null;
            return WorkOperation<CardCoverContent>.Success(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is OperationCanceledException or AttachmentStorageException or IOException or UnauthorizedAccessException)
        { return WorkOperation<CardCoverContent>.Failure("work_storage_unavailable"); }
        finally { if (prepared is not null) await prepared.DisposeAsync(); }
    }
}
