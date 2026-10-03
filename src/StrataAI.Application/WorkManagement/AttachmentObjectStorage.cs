namespace StrataAI.Application.WorkManagement;

// An object reference is derived only from server-owned Organization/attachment
// identity. No filename, request path or caller-provided key can select a file.
public sealed record AttachmentObjectReference
{
    public AttachmentObjectReference(Guid organizationId, Guid attachmentId)
    {
        if (organizationId == Guid.Empty || attachmentId == Guid.Empty)
            throw new ArgumentException("Attachment object scope and identity are required.");
        OrganizationId = organizationId; AttachmentId = attachmentId;
    }
    public Guid OrganizationId { get; }
    public Guid AttachmentId { get; }
    public string ObjectKey => $"attachments/{OrganizationId:N}/{AttachmentId:N}";
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
