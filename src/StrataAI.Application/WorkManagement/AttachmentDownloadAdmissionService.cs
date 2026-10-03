using System.Text.Json.Serialization;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record AttachmentDownloadOptions(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    Guid AttachmentId, long AttachmentVersion, Guid ActorId);

// A server-only snapshot for controlled delivery, never a bearer credential.
// Every use requires current authorization again after provider preparation.
public sealed class AttachmentDownloadAdmission
{
    internal AttachmentDownloadAdmission(Guid actor, CardRecord card, AttachmentFileRecord file, DateTimeOffset at)
    { ActorId = actor; Card = card; File = file; AdmittedAt = at; ExpiresAt = at.AddMinutes(1); }
    [JsonIgnore] public Guid ActorId { get; }
    [JsonIgnore] public CardRecord Card { get; }
    [JsonIgnore] public AttachmentFileRecord File { get; }
    [JsonIgnore] public DateTimeOffset AdmittedAt { get; }
    [JsonIgnore] public DateTimeOffset ExpiresAt { get; }
}

public sealed class AttachmentDownloadAdmissionService(IWorkManagementStore work, IAttachmentMetadataStore attachments,
    IOrganizationStore organizations, IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions, IClock clock)
{
    public async Task<WorkOperation<AttachmentDownloadAdmission>> AdmitAsync(Guid cardId, Guid attachmentId,
        Guid actor, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null || attachmentId == Guid.Empty)
            return WorkOperation<AttachmentDownloadAdmission>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, false, ct), async () =>
            {
                var current = await work.FindCardAsync(cardId, ct);
                var file = await attachments.FindFileAttachmentAsync(hint.OrganizationId, cardId, attachmentId, ct);
                return current is not null && IsDeliverable(file, current, attachmentId)
                    ? WorkOperation<AttachmentDownloadAdmission>.Success(new(actor, current, file!, clock.UtcNow))
                    : WorkOperation<AttachmentDownloadAdmission>.Failure("card_not_found");
            }, ct);
    }

    // No provider I/O occurs inside this owning database scope. Preparing bytes
    // cannot extend this snapshot or authorize a changed/moved/deleted file.
    public async Task<WorkOperation<bool>> RevalidateAsync(AttachmentDownloadAdmission admission, Guid actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        bool Timely() => clock.UtcNow >= admission.AdmittedAt && clock.UtcNow < admission.ExpiresAt;
        if (actor != admission.ActorId || !Timely()) return WorkOperation<bool>.Failure("card_not_found");
        var hint = admission.Card;
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            async () => Timely() && await AttachmentScope(), async () =>
            {
                var current = await attachments.FindFileAttachmentAsync(hint.OrganizationId, hint.Id, admission.File.Metadata.Id, ct);
                return current == admission.File && IsDeliverable(current, hint, admission.File.Metadata.Id)
                    ? WorkOperation<bool>.Success(true) : WorkOperation<bool>.Failure("card_not_found");
            }, ct);

        async Task<bool> AttachmentScope() => await AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, false, ct);
    }

    private static bool IsDeliverable(AttachmentFileRecord? file, CardRecord card, Guid id) => file is not null
        && file.Metadata.Id == id && file.Metadata.OrganizationId == card.OrganizationId && file.Metadata.CardId == card.Id
        && file.Metadata.Kind == AttachmentKind.File && file.Metadata.ScanStatus == AttachmentScanStatus.Clean
        && file.Metadata.DeletedAt is null && file.Metadata.ScannedAt is not null && file.Metadata.Version >= 2
        && file.Metadata.ScannedAt >= file.Metadata.CreatedAt && file.Metadata.ScannedAt <= file.Metadata.UpdatedAt
        && file.Metadata.Url is null && file.Metadata.MimeType is "image/png" or "image/jpeg" or "image/webp" or "application/pdf"
        && file.Metadata.SizeBytes is > 0 and <= 1_073_741_824 && file.Metadata.SizeBytes == file.Integrity.SizeBytes
        && !file.Integrity.Reference.IsPreview
        && file.Integrity.Reference.OrganizationId == card.OrganizationId && file.Integrity.Reference.AttachmentId == id;

    public async Task<WorkOperation<AttachmentPreviewAdmission>> AdmitPreviewAsync(Guid cardId, Guid attachmentId,
        Guid actor, CancellationToken ct = default)
    {
        var source = await AdmitAsync(cardId, attachmentId, actor, ct);
        if (!source.Succeeded || source.Value is null)
            return WorkOperation<AttachmentPreviewAdmission>.Failure(source.ErrorCode ?? "card_not_found");
        var current = source.Value;
        return await transactions.ExecuteReadAsync(current.Card.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, current.Card, actor, false, ct), async () =>
            {
                var file = await attachments.FindFileAttachmentAsync(current.Card.OrganizationId, cardId, attachmentId, ct);
                if (file is null || file != current.File || file.Metadata.MimeType is not ("image/png" or "image/jpeg" or "image/webp"))
                    return WorkOperation<AttachmentPreviewAdmission>.Failure("card_not_found");
                var preview = await attachments.FindPublishedPreviewAsync(file, ct);
                return IsPreview(preview, current)
                    ? WorkOperation<AttachmentPreviewAdmission>.Success(new(current, preview!))
                    : WorkOperation<AttachmentPreviewAdmission>.Failure("card_not_found");
            }, ct);
    }

    public async Task<WorkOperation<bool>> RevalidatePreviewAsync(AttachmentPreviewAdmission admitted, Guid actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        var source = admitted.Source;
        bool Timely() => actor == source.ActorId && clock.UtcNow >= source.AdmittedAt && clock.UtcNow < source.ExpiresAt;
        if (!Timely()) return WorkOperation<bool>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(source.Card.OrganizationId, actor, "card_not_found",
            async () => Timely() && await AttachmentAdmission.CheckAsync(work, organizations, boards, source.Card, actor, false, ct), async () =>
            {
                var file = await attachments.FindFileAttachmentAsync(source.Card.OrganizationId, source.Card.Id, source.File.Metadata.Id, ct);
                if (file is null || file != source.File || !IsDeliverable(file, source.Card, source.File.Metadata.Id))
                    return WorkOperation<bool>.Failure("card_not_found");
                var preview = await attachments.FindPublishedPreviewAsync(file, ct);
                return Timely() && preview == admitted.Preview && IsPreview(preview, source)
                    ? WorkOperation<bool>.Success(true) : WorkOperation<bool>.Failure("card_not_found");
            }, ct);
    }

    private static bool IsPreview(AttachmentPublishedPreview? preview, AttachmentDownloadAdmission source) => preview is not null
        && preview.Integrity.Reference.IsPreview && preview.Integrity.Reference.OrganizationId == source.Card.OrganizationId
        && preview.Integrity.Reference.AttachmentId != source.File.Metadata.Id
        && preview.Integrity.SizeBytes is >= 45 and <= 8_388_608
        && preview.Width is >= 1 and <= 1024 && preview.Height is >= 1 and <= 1024;
}
