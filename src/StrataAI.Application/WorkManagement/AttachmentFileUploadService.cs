using System.Security.Cryptography;
using StrataAI.Application.Common;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// All provider I/O follows committed admission and precedes a fresh short
// command. No caller can supply a provider key, measurement or clean verdict.
public sealed class AttachmentFileUploadService(AttachmentUploadAdmissionService admission,
    AttachmentFilePublicationService publication, IAttachmentObjectStorage storage,
    AttachmentUploadPolicy policy, IAttachmentFileTypeInspector inspector, IClock clock)
{
    public async Task<WorkOperation<AttachmentChange>> UploadAsync(Guid cardId, Guid actor, Guid retryKey,
        PrepareAttachmentUploadInput input, Stream source, string correlationId, CancellationToken ct = default)
    {
        var prepared = await admission.PrepareAsync(cardId, actor, retryKey, input, ct);
        if (!prepared.Succeeded || prepared.Value is null) return Fail(prepared.ErrorCode);
        var value = prepared.Value;
        if (value.State == AttachmentUploadState.Writing)
        {
            var retired = await admission.ExpireWriterAsync(cardId, actor, value, ct);
            if (!retired.Succeeded || retired.Value is null) return Fail(retired.ErrorCode);
            value = retired.Value;
        }
        if (value.State == AttachmentUploadState.Reconcile)
        {
            using var recovery = CancellationTokenSource.CreateLinkedTokenSource(ct);
            recovery.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                var reference = new AttachmentObjectReference(value.OrganizationId, value.Id);
                await using var existing = await storage.OpenPrivateReadAsync(reference, recovery.Token);
                WorkOperation<AttachmentUploadRecord> reconciled;
                if (existing is null)
                    reconciled = await admission.ConfirmMissingAsync(cardId, actor, value, recovery.Token);
                else
                {
                    await using var inspected = await AttachmentUploadReader.OpenAsync(existing, policy, inspector, recovery.Token);
                    var measured = await MeasureAsync(inspected.Content, reference, value.ExpectedSizeBytes, recovery.Token);
                    reconciled = await admission.RecordReconciledAsync(cardId, actor, value, inspected.Type, measured, recovery.Token);
                }
                if (!reconciled.Succeeded || reconciled.Value is null) return Fail(reconciled.ErrorCode);
                value = reconciled.Value;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (AttachmentUploadValidationException error) { return Fail(error.Code); }
            catch (Exception error) when (Recoverable(error)) { return Fail("work_storage_unavailable"); }
        }
        if (value.State == AttachmentUploadState.Prepared)
        {
            var claimed = await admission.ClaimAsync(cardId, actor, value.Id, retryKey, value.Version, ct);
            if (!claimed.Succeeded || claimed.Value is null) return Fail(claimed.ErrorCode);
            var writer = claimed.Value;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = writer.WriteLeaseUntil!.Value - clock.UtcNow - TimeSpan.FromSeconds(5);
            if (remaining <= TimeSpan.Zero) return Fail("attachment_upload_unavailable");
            deadline.CancelAfter(remaining < TimeSpan.FromMinutes(5) ? remaining : TimeSpan.FromMinutes(5));
            try
            {
                await using var inspected = await AttachmentUploadReader.OpenAsync(source, policy, inspector, deadline.Token);
                var measured = await storage.WritePrivateAsync(new(writer.OrganizationId, writer.Id), inspected.Content,
                    writer.ExpectedSizeBytes, deadline.Token);
                var stored = await admission.RecordStoredAsync(cardId, actor, writer, inspected.Type, measured, deadline.Token);
                if (!stored.Succeeded || stored.Value is null)
                {
                    await RetainUnknownAsync(cardId, actor, writer);
                    return Fail(stored.ErrorCode);
                }
                value = stored.Value;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            { await RetainUnknownAsync(cardId, actor, writer); throw; }
            catch (AttachmentUploadValidationException error)
            { await RetainUnknownAsync(cardId, actor, writer); return Fail(error.Code); }
            catch (AttachmentStorageException error) when (error.Code == "object_too_large")
            { await RetainUnknownAsync(cardId, actor, writer); return Fail("attachment_too_large"); }
            catch (Exception error) when (Recoverable(error))
            { await RetainUnknownAsync(cardId, actor, writer); return Fail("work_storage_unavailable"); }
        }
        if (value.State is not (AttachmentUploadState.Stored or AttachmentUploadState.Published))
            return Fail("attachment_upload_unavailable");
        return await publication.PublishAsync(cardId, actor, value.Id, retryKey, correlationId, ct);
    }

    private async Task RetainUnknownAsync(Guid cardId, Guid actor, AttachmentUploadRecord writer)
    {
        // Independent bounded bookkeeping after request cancellation. Current
        // permission still applies; failure leaves the original expiring writer
        // for later recovery. Never delete a possibly committed final object.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await admission.MarkUnknownWriteAsync(cardId, actor, writer, cleanup.Token); }
        catch (Exception error) when (Recoverable(error)) { }
    }
    private static async Task<StoredAttachmentObject> MeasureAsync(Stream source, AttachmentObjectReference reference, long expected, CancellationToken ct)
    {
        var buffer = new byte[65536]; long count = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, expected - count + 1)), ct);
                if (read == 0) break;
                count += read;
                if (count > expected) throw new AttachmentUploadValidationException("attachment_integrity_invalid");
                hash.AppendData(buffer, 0, read);
            }
            if (count != expected) throw new AttachmentUploadValidationException("attachment_integrity_invalid");
            return new(reference, count, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static bool Recoverable(Exception error) => error is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    private static WorkOperation<AttachmentChange> Fail(string? code) => WorkOperation<AttachmentChange>.Failure(code ?? "work_storage_unavailable");
}
