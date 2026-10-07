using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class UnavailableAttachmentDownloadPreparer : IAttachmentDownloadPreparer
{
    public Task<Stream?> PrepareAsync(AttachmentScanRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream?>(null);
    }
}
