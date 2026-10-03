namespace StrataAI.Application.WorkManagement;

// References derive only from server-owned Organization/object identity.
// Original and preview namespaces are fixed; no caller-provided key or prefix.
public sealed record AttachmentObjectReference
{
    public AttachmentObjectReference(Guid organizationId, Guid attachmentId) : this(organizationId, attachmentId, false) { }
    private AttachmentObjectReference(Guid organizationId, Guid attachmentId, bool isPreview)
    {
        if (organizationId == Guid.Empty || attachmentId == Guid.Empty)
            throw new ArgumentException("Attachment object scope and identity are required.");
        OrganizationId = organizationId; AttachmentId = attachmentId; IsPreview = isPreview;
    }
    public Guid OrganizationId { get; }
    public Guid AttachmentId { get; }
    public bool IsPreview { get; }
    public static AttachmentObjectReference ForPreview(Guid organizationId, Guid previewJobId) => new(organizationId, previewJobId, true);
    public string ObjectKey => $"{(IsPreview ? "attachment-previews" : "attachments")}/{OrganizationId:N}/{AttachmentId:N}";
}

public sealed record StoredAttachmentObject(AttachmentObjectReference Reference, long SizeBytes, string Sha256);
public sealed class AttachmentStorageException(string code) : Exception("Attachment object storage is unavailable.")
{
    public string Code { get; } = code is "object_exists" or "object_too_large" or "object_empty" or "object_storage_unavailable" or "object_scope_invalid" or "object_path_unsafe"
        ? code : throw new ArgumentException("A fixed attachment storage failure code is required.", nameof(code));
}

public interface IAttachmentObjectStorage
{
    // All objects are private. These primitives do not authorize a caller or
    // mark bytes clean. Application must admit current rights/parent lifecycle
    // before use; Worker must verify its durable job scope before scanner reads.
    // Caller owns the source; returned read streams belong to the caller.
    Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximumBytes, CancellationToken ct);
    Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct);
    Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct);
}
