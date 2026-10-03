using StrataAI.Domain.WorkManagement;
using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

// Internal command/read data. HTTP responses must project current-authorized
// fields; neither provider object keys nor scanner diagnostics belong here.
public sealed record AttachmentMetadata(Guid Id, Guid OrganizationId, Guid CardId, Guid UploaderId,
    AttachmentKind Kind, string DisplayName, string? MimeType, long? SizeBytes, string? Url,
    AttachmentScanStatus ScanStatus, DateTimeOffset? ScannedAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, long Version, DateTimeOffset? DeletedAt)
{
    public AttachmentLifecycleState LifecycleState { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public Guid? DeletedBy { get; init; }
}

// Private Application/Worker record. Never serialize the integrity request into
// normal attachment pages/receipts or admit it from an HTTP request body.
public sealed record AttachmentFileRecord(AttachmentMetadata Metadata, [property: JsonIgnore] AttachmentScanRequest Integrity);

public interface IAttachmentMetadataStore
{
    // Unsupported providers fail closed. A declaration alone is never published.
    Task<AttachmentPublishedPreview?> FindPublishedPreviewAsync(AttachmentFileRecord source, CancellationToken ct)
        => Task.FromResult<AttachmentPublishedPreview?>(null);
    // Production calls require an owning tenant read/command transaction.
    // Authorization, parent lifecycle/CAS, audit/events and idempotency belong
    // to the enclosing Application command, not these metadata primitives.
    Task<AttachmentMetadata> CreateUrlAttachmentAsync(Guid id, Guid organization, Guid card, Guid uploader,
        string title, string url, DateTimeOffset now, CancellationToken ct);
    Task<AttachmentMetadata> CreateFileAttachmentAsync(StoredAttachmentObject measured, Guid card, Guid uploader,
        string displayName, string verifiedMimeType, DateTimeOffset now, CancellationToken ct);
    Task<AttachmentFileRecord?> FindFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct);
    // Separate protected archive review. Ordinary file lookup stays Active-only.
    Task<AttachmentFileRecord?> FindArchivedFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
        => Task.FromResult<AttachmentFileRecord?>(null);
    Task<AttachmentMetadata?> FindAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct);
    // Includes tombstones only inside explicitly authorized lifecycle commands.
    Task<AttachmentMetadata?> FindLifecycleAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct);
    Task<IReadOnlyList<AttachmentMetadata>> ListArchivedAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct);
    Task<AttachmentMetadata?> ChangeAttachmentLifecycleAsync(Guid organization, Guid card, Guid attachment,
        long version, AttachmentLifecycleState from, AttachmentLifecycleState to, Guid actor, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<AttachmentMetadata>> ListAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct);
}

public static class AttachmentMetadataMapping
{
    public static AttachmentMetadata From(Attachment value) => new(value.Id, value.OrganizationId, value.CardId,
        value.UploaderId, value.Kind, value.DisplayName, value.MimeType, value.SizeBytes, value.Url,
        value.ScanStatus, value.ScannedAt, value.CreatedAt, value.UpdatedAt, value.Version, value.DeletedAt)
    { LifecycleState = value.LifecycleState, ArchivedAt = value.ArchivedAt, DeletedBy = value.DeletedBy };

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
