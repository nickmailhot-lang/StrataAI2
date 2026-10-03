using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.WorkManagement;

public enum AttachmentPreviewCompletion { Applied, Superseded, LeaseLost }
public interface IAttachmentPreviewPublicationStore
{
    // Exact live claim/current canonical source and declared output are checked
    // again before atomic receipt, revisions, audit/current Board event effects.
    Task<AttachmentPreviewCompletion> FinishAsync(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt,
        AttachmentPreviewStoredOutput output, CancellationToken ct);
}

public sealed class AttachmentPreviewDeliveryHandler(IAttachmentPreviewIntentStore intents,
    AttachmentPreviewStorageRecovery recovery, IAttachmentPreviewPublicationStore publication) : IBackgroundJobHandler
{
    public string JobType => AttachmentPreviewJobs.Type;
    public string ServiceIdentity => AttachmentPreviewJobs.Service;
    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken ct)
    {
        if (job.JobType != JobType || job.ServiceIdentity != ServiceIdentity || job.Id == Guid.Empty
            || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty || job.WorkerId == Guid.Empty
            || job.LeaseId == Guid.Empty || job.AttemptCount < 1) throw Unavailable();
        var attempt = AttachmentPreviewAttempt.Parse(job.SafeMetadataJson);
        if (job.Id == attempt.AttachmentId) throw Unavailable();
        ct.ThrowIfCancellationRequested();
        var loaded = await intents.LoadAsync(job, attempt, ct);
        if (loaded.Status is AttachmentPreviewLoadStatus.Applied or AttachmentPreviewLoadStatus.Superseded)
        {
            if (loaded.Source is not null || loaded.VerifiedMimeType is not null || loaded.DeclaredOutput is not null) throw Unavailable();
            return;
        }
        if (loaded.Status != AttachmentPreviewLoadStatus.Ready || loaded.Source is not { } source
            || source.Reference.OrganizationId != job.OrganizationId || source.Reference.AttachmentId != attempt.AttachmentId
            || loaded.VerifiedMimeType is not ("image/png" or "image/jpeg" or "image/webp")) throw Unavailable();
        var stored = await recovery.EnsureStoredAsync(job, attempt, ct);
        if (stored is null) return;
        ct.ThrowIfCancellationRequested();
        if (stored.Reference.OrganizationId != job.OrganizationId || stored.Reference.AttachmentId != job.Id
            || await publication.FinishAsync(job, attempt, stored, ct) is not (AttachmentPreviewCompletion.Applied or AttachmentPreviewCompletion.Superseded))
            throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("Attachment preview delivery is unavailable.");
}
