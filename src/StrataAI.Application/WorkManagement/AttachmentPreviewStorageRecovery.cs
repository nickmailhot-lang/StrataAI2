using System.Text.Json.Serialization;
using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.WorkManagement;

public sealed record AttachmentPreviewStoredOutput([property: JsonIgnore] AttachmentObjectReference Reference,
    [property: JsonIgnore] AttachmentPreviewMeasurement Measurement);

// This coordinator establishes private storage evidence, not publication or a
// display grant. The delivery handler must separately fence its final effects.
public sealed class AttachmentPreviewStorageRecovery(IAttachmentPreviewIntentStore intents,
    IAttachmentDownloadPreparer preparer, IAttachmentImagePreviewGenerator generator, IAttachmentObjectStorage objects)
{
    public async Task<AttachmentPreviewStoredOutput?> EnsureStoredAsync(ClaimedBackgroundJob job,
        AttachmentPreviewAttempt attempt, CancellationToken ct)
    {
        if (job.JobType != AttachmentPreviewJobs.Type || job.ServiceIdentity != AttachmentPreviewJobs.Service
            || job.Id == Guid.Empty || job.Id == attempt.AttachmentId || job.OrganizationId == Guid.Empty
            || job.ActorId == Guid.Empty || job.WorkerId == Guid.Empty || job.LeaseId == Guid.Empty || job.AttemptCount < 1
            || attempt != AttachmentPreviewAttempt.Parse(job.SafeMetadataJson)) throw Unavailable();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(55)); var token = deadline.Token;
        var admitted = await intents.LoadAsync(job, attempt, token);
        if (admitted.Status is AttachmentPreviewLoadStatus.Superseded or AttachmentPreviewLoadStatus.Applied)
        {
            if (admitted.Source is not null || admitted.VerifiedMimeType is not null || admitted.DeclaredOutput is not null) throw Unavailable();
            return null;
        }
        RequireReady(admitted, job, attempt);
        var source = admitted.Source!; var mime = admitted.VerifiedMimeType!;
        var artifact = AttachmentObjectReference.ForPreview(job.OrganizationId, job.Id);
        var output = admitted.DeclaredOutput;
        async Task<bool> StillAdmitted(AttachmentPreviewMeasurement? expected)
        {
            token.ThrowIfCancellationRequested();
            var current = await intents.LoadAsync(job, attempt, token);
            if (current.Status is AttachmentPreviewLoadStatus.Superseded or AttachmentPreviewLoadStatus.Applied)
            {
                if (current.Source is not null || current.VerifiedMimeType is not null || current.DeclaredOutput is not null) throw Unavailable();
                return false;
            }
            RequireReady(current, job, attempt);
            if (current.Source != source || current.VerifiedMimeType != mime || current.DeclaredOutput != expected) throw Unavailable();
            return true;
        }
        async Task<bool> ExistsVerified(AttachmentPreviewMeasurement expected)
        {
            // The preparer reads complete bytes/SHA/EOF, not provider metadata.
            await using var existing = await preparer.PrepareAsync(new(artifact, expected.SizeBytes, expected.Sha256), token);
            return existing is not null;
        }
        if (output is not null)
        {
            if (!await StillAdmitted(output)) return null;
            if (await ExistsVerified(output))
                return await StillAdmitted(output) ? new(artifact, output) : null;
        }
        AttachmentPreviewImage? image = null;
        try
        {
            // Existing missing objects can be retried only with the originally
            // declared encoding. A codec change cannot overwrite the ledger.
            if (!await StillAdmitted(output)) return null;
            await using (var original = await preparer.PrepareAsync(source, token))
            {
                if (original is null) throw Unavailable();
                image = await generator.GenerateAsync(source, mime, original, token);
            }
            var generated = new AttachmentPreviewMeasurement(image.SizeBytes, image.Sha256, image.Width, image.Height);
            if (output is not null && generated != output) throw Unavailable();
            output = generated; token.ThrowIfCancellationRequested();
            var declaration = await intents.DeclareAsync(job, attempt, source, mime, output, token);
            if (declaration is AttachmentPreviewDeclaration.Superseded or AttachmentPreviewDeclaration.Applied) return null;
            if (declaration != AttachmentPreviewDeclaration.Declared) throw Unavailable();
            if (!await StillAdmitted(output)) return null;
            // A previous write may have committed even when the client saw a
            // failure. Reconcile it before any conditional, non-clobbering write.
            if (!await ExistsVerified(output))
            {
                if (!await StillAdmitted(output)) return null;
                image.Bytes.Position = 0;
                var stored = await objects.WritePrivateAsync(artifact, image.Bytes, output.SizeBytes, token);
                if (stored.Reference != artifact || stored.SizeBytes != output.SizeBytes || stored.Sha256 != output.Sha256) throw Unavailable();
                if (!await StillAdmitted(output)) return null;
                if (!await ExistsVerified(output)) throw Unavailable();
            }
            return await StillAdmitted(output) ? new(artifact, output) : null;
        }
        finally { image?.Dispose(); }
    }
    private static void RequireReady(AttachmentPreviewLoad loaded, ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt)
    {
        if (loaded.Status != AttachmentPreviewLoadStatus.Ready || loaded.Source is not { } source
            || source.Reference.IsPreview || source.Reference.OrganizationId != job.OrganizationId || source.Reference.AttachmentId != attempt.AttachmentId
            || loaded.VerifiedMimeType is not ("image/png" or "image/jpeg" or "image/webp")) throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("Attachment preview storage recovery is unavailable.");
}
