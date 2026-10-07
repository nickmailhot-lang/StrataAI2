using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Disabled storage must compose without credentials or a provider. Accidental
// calls fail closed: no operation reports that bytes were stored or deleted.
public sealed class UnavailableAttachmentObjectStorage : IAttachmentObjectStorage
{
    public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference,
        Stream source, long maximumBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new AttachmentStorageException("object_storage_unavailable");
    }

    public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new AttachmentStorageException("object_storage_unavailable");
    }

    public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new AttachmentStorageException("object_storage_unavailable");
    }
}
