using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// Internal command/read data. HTTP responses must project current-authorized
// fields; neither provider object keys nor scanner diagnostics belong here.
public sealed record AttachmentMetadata(Guid Id, Guid OrganizationId, Guid CardId, Guid UploaderId,
    AttachmentKind Kind, string DisplayName, string? MimeType, long? SizeBytes, string? Url,
    AttachmentScanStatus ScanStatus, DateTimeOffset? ScannedAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, long Version, DateTimeOffset? DeletedAt);

// Private Application/Worker record. Never serialize the integrity request into
// normal attachment pages/receipts or admit it from an HTTP request body.
public sealed record AttachmentFileRecord(AttachmentMetadata Metadata, AttachmentScanRequest Integrity);

public interface IAttachmentMetadataStore
{
    // Production calls require an owning tenant read/command transaction.
    // Authorization, parent lifecycle/CAS, audit/events and idempotency belong
    // to the enclosing Application command, not these metadata primitives.
    Task<AttachmentMetadata> CreateUrlAttachmentAsync(Guid id, Guid organization, Guid card, Guid uploader,
        string title, string url, DateTimeOffset now, CancellationToken ct);
    Task<AttachmentMetadata> CreateFileAttachmentAsync(StoredAttachmentObject measured, Guid card, Guid uploader,
        string displayName, string verifiedMimeType, DateTimeOffset now, CancellationToken ct);
    Task<AttachmentFileRecord?> FindFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct);
    Task<AttachmentMetadata?> FindAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct);
    Task<IReadOnlyList<AttachmentMetadata>> ListAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct);
}

public static class AttachmentMetadataMapping
{
    public static AttachmentMetadata From(Attachment value) => new(value.Id, value.OrganizationId, value.CardId,
        value.UploaderId, value.Kind, value.DisplayName, value.MimeType, value.SizeBytes, value.Url,
        value.ScanStatus, value.ScannedAt, value.CreatedAt, value.UpdatedAt, value.Version, value.DeletedAt);

    public static DateTimeOffset DatabaseTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    public static void RequireCursor(DateTimeOffset? createdAt, Guid? id)
    {
        if (createdAt.HasValue != id.HasValue || id == Guid.Empty)
            throw new ArgumentException("Attachment cursor must contain both timestamp and identity.");
    }
}
