namespace StrataAI.Application.WorkManagement;

public sealed record AttachmentPreviewBackfillResult(int Visited, int Enqueued);

/// <summary>Worker-only metadata maintenance; never reads or changes provider objects.</summary>
public interface IAttachmentPreviewBackfillStore
{
    Task<AttachmentPreviewBackfillResult> EnqueuePageAsync(Guid organizationId, int maximumRows, CancellationToken cancellationToken);
}
