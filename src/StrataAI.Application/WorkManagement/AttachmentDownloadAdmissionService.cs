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
        && file.Integrity.Reference.OrganizationId == card.OrganizationId && file.Integrity.Reference.AttachmentId == id;
}
