using StrataAI.Application.BackgroundJobs;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public enum AttachmentScanLoadStatus { Ready, Applied, Superseded, LeaseLost }
public sealed record AttachmentScanLoad(AttachmentScanLoadStatus Status, AttachmentScanRequest? Request = null);
public enum AttachmentScanCompletion { Applied, Superseded, Retry, LeaseLost }

public interface IAttachmentScanDeliveryStore
{
    // A short transaction proves the exact canonical queue claim before any
    // provider read. Only Ready discloses private size/digest; no object keys.
    Task<AttachmentScanLoad> LoadAsync(ClaimedBackgroundJob job, AttachmentScanAttempt attempt, CancellationToken ct);
    // After provider I/O, revalidate live lease and original file measurement/
    // revision, then atomically CAS status, current Card revision, audit/event.
    // Failed evidence retries with no effects until the canonical final attempt.
    Task<AttachmentScanCompletion> FinishAsync(ClaimedBackgroundJob job, AttachmentScanAttempt attempt,
        AttachmentScanEvidence evidence, CancellationToken ct);
}

public sealed class AttachmentScanDeliveryHandler(IAttachmentScanDeliveryStore delivery, AttachmentQuarantineScanner scanner)
    : IBackgroundJobHandler
{
    public string JobType => AttachmentScanJobs.Type;
    public string ServiceIdentity => AttachmentScanJobs.Service;
    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        if (job.JobType != JobType || job.ServiceIdentity != ServiceIdentity || job.Id == Guid.Empty
            || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty || job.WorkerId == Guid.Empty
            || job.LeaseId == Guid.Empty || job.AttemptCount < 1) throw Unavailable();
        var attempt = AttachmentScanAttempt.Parse(job.SafeMetadataJson); cancellationToken.ThrowIfCancellationRequested();
        var loaded = await delivery.LoadAsync(job,attempt,cancellationToken);
        if (loaded.Status is AttachmentScanLoadStatus.Applied or AttachmentScanLoadStatus.Superseded)
        {
            if (loaded.Request is not null) throw Unavailable();
            return;
        }
        if (loaded.Status != AttachmentScanLoadStatus.Ready || loaded.Request is not { } request
            || request.Reference.OrganizationId != job.OrganizationId || request.Reference.AttachmentId != attempt.AttachmentId)
            throw Unavailable();
        cancellationToken.ThrowIfCancellationRequested();
        // Integrity evidence is bound to the complete private stream and this
        // persisted request. A verdict alone cannot become a Clean publication.
        var evidence = await scanner.ScanAsync(request,cancellationToken);
        if (evidence.Request != request || evidence.Status is not (AttachmentScanStatus.Clean or AttachmentScanStatus.Rejected or AttachmentScanStatus.Failed))
            throw Unavailable();
        cancellationToken.ThrowIfCancellationRequested();
        var result = await delivery.FinishAsync(job,attempt,evidence,cancellationToken);
        if (result is not (AttachmentScanCompletion.Applied or AttachmentScanCompletion.Superseded)) throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("Attachment scan delivery is unavailable.");
}
