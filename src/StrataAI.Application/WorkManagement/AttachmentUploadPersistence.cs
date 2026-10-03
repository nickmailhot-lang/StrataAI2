using System.Text.Json.Serialization;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// Private command data, never an HTTP response/request contract.
public sealed record AttachmentUploadRecord(Guid Id, Guid OrganizationId, Guid CardId, Guid UploaderId,
    Guid RetryKey, long OriginalCardVersion, string DisplayName, long ExpectedSizeBytes,
    [property: JsonIgnore] string ExpectedSha256, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt, long Version, AttachmentUploadState State, Guid? WriteLeaseId,
    DateTimeOffset? WriteLeaseUntil, string? VerifiedMimeType, DateTimeOffset? StoredAt,
    DateTimeOffset? PublishedAt, DateTimeOffset? AbandonedAt)
{
    public static AttachmentUploadRecord From(AttachmentUploadIntent value) => new(value.Id, value.OrganizationId,
        value.CardId, value.UploaderId, value.RetryKey, value.CardVersion, value.DisplayName, value.ExpectedSizeBytes,
        value.ExpectedSha256, value.CreatedAt, value.UpdatedAt, value.ExpiresAt, value.Version, value.State,
        value.WriteLeaseId, value.WriteLeaseUntil, value.VerifiedMimeType, value.StoredAt, value.PublishedAt, value.AbandonedAt);
}

public enum AttachmentUploadAction { StartWrite, RenewWrite, UnknownWrite, ExpiredWriter, ConfirmMissing, RecordStored, RecordReconciled, Abandon, Publish }

// Internal server-owned operation. Measured bytes originate only in the private
// adapter; ConfirmMissing requires verified provider absence, not an exception.
public sealed record AttachmentUploadChange(AttachmentUploadAction Action, DateTimeOffset At,
    Guid? LeaseId = null, DateTimeOffset? LeaseUntil = null,
    StoredAttachmentObject? Measured = null, string? VerifiedMimeType = null)
{
    public void Validate(Guid organization, Guid attachment)
    {
        if (!Enum.IsDefined(Action)) throw new ArgumentOutOfRangeException(nameof(Action));
        var writer = Action is AttachmentUploadAction.StartWrite or AttachmentUploadAction.RenewWrite;
        var leased = writer || Action is AttachmentUploadAction.UnknownWrite or AttachmentUploadAction.RecordStored;
        var measured = Action is AttachmentUploadAction.RecordStored or AttachmentUploadAction.RecordReconciled;
        if (leased != LeaseId.HasValue || LeaseId == Guid.Empty || writer != LeaseUntil.HasValue
            || measured != (Measured is not null) || measured != (VerifiedMimeType is not null))
            throw new ArgumentException("Upload transition inputs are invalid.");
        var at = AttachmentMetadataMapping.DatabaseTimestamp(At);
        var until = LeaseUntil.HasValue ? AttachmentMetadataMapping.DatabaseTimestamp(LeaseUntil.Value) : (DateTimeOffset?)null;
        if (writer && (until <= at || until - at > TimeSpan.FromMinutes(10)))
            throw new ArgumentException("Upload lease window is invalid.");
        if (Measured is not null)
        {
            if (Measured.Reference is null || Measured.Reference.OrganizationId != organization || Measured.Reference.AttachmentId != attachment)
                throw new ArgumentException("Measured upload identity is invalid.");
            var verified = Attachment.QuarantineFile(attachment, organization, attachment, attachment, "Verification",
                VerifiedMimeType!, Measured.SizeBytes, Measured.Reference.ObjectKey, Measured.Sha256, At);
            if (verified.MimeType != VerifiedMimeType) throw new ArgumentException("Verified upload type must be canonical.");
        }
    }
}

public interface IAttachmentUploadIntentStore
{
    // Every production method requires the enclosing owning tenant transaction.
    // Current permission/lifecycle, original Card CAS, publication metadata,
    // audit/events/scan job and provider I/O are Application responsibilities.
    Task<AttachmentUploadRecord?> PrepareUploadAsync(AttachmentUploadIntent prepared, CancellationToken ct);
    Task<AttachmentUploadRecord?> FindUploadByRetryAsync(Guid organization, Guid uploader, Guid retryKey, CancellationToken ct);
    Task<AttachmentUploadRecord?> TryChangeUploadAsync(Guid organization, Guid card, Guid uploader, Guid attachment,
        long expectedVersion, AttachmentUploadChange change, CancellationToken ct);
}
