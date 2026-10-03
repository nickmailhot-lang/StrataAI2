using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// Expected digest binds the original request; it is not verified content.
public sealed record PrepareAttachmentUploadInput(string DisplayName, long SizeBytes, string Sha256, long CardVersion);
public sealed record AttachmentUploadOptions(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    long MaximumBytes, IReadOnlyList<string> AllowedMimeTypes);

public sealed class AttachmentUploadAdmissionService(IWorkManagementStore work, IOrganizationStore organizations,
    IWorkBoardAuthorization boards, IAttachmentUploadIntentStore uploads, IWorkManagementUnitOfWork transactions,
    IClock clock, AttachmentUploadPolicy policy)
{
    public async Task<WorkOperation<AttachmentUploadOptions>> GetOptionsAsync(Guid cardId, Guid actor, CancellationToken ct)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentUploadOptions>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct), async () =>
            {
                var current = await work.FindCardAsync(cardId, ct);
                return current is null ? WorkOperation<AttachmentUploadOptions>.Failure("card_not_found")
                    : WorkOperation<AttachmentUploadOptions>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version,
                        policy.MaximumBytes, policy.AllowedMimeTypes.Order(StringComparer.Ordinal).ToArray()));
            }, ct);
    }

    public async Task<WorkOperation<bool>> CheckRequestAsync(Guid cardId, Guid actor, CancellationToken ct)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<bool>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct),
            () => Task.FromResult(WorkOperation<bool>.Success(true)), ct);
    }

    public async Task<WorkOperation<AttachmentUploadRecord>> PrepareAsync(Guid cardId, Guid actor, Guid retryKey,
        PrepareAttachmentUploadInput input, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentUploadRecord>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct), async () =>
            {
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (input is null) return WorkOperation<AttachmentUploadRecord>.Failure("invalid_attachment_upload");
                AttachmentUploadIntent proposed;
                try
                {
                    proposed = AttachmentUploadIntent.Prepare(Guid.NewGuid(), hint.OrganizationId, cardId, actor, retryKey,
                        input.CardVersion, input.DisplayName, input.SizeBytes, input.Sha256, now.AddHours(1), now);
                }
                catch (ArgumentException) { return WorkOperation<AttachmentUploadRecord>.Failure("invalid_attachment_upload"); }
                if (input.SizeBytes > policy.MaximumBytes) return WorkOperation<AttachmentUploadRecord>.Failure("attachment_too_large");
                var existing = await uploads.FindUploadByRetryAsync(hint.OrganizationId, actor, retryKey, ct);
                if (existing is null)
                {
                    var current = await work.FindCardAsync(cardId, ct);
                    if (current is null || current.Version != input.CardVersion) return WorkOperation<AttachmentUploadRecord>.Failure("version_conflict");
                    existing = await uploads.PrepareUploadAsync(proposed, ct)
                        ?? await uploads.FindUploadByRetryAsync(hint.OrganizationId, actor, retryKey, ct);
                }
                if (existing is null) return WorkOperation<AttachmentUploadRecord>.Failure("work_storage_unavailable");
                if (existing.Id == Guid.Empty || existing.OrganizationId != hint.OrganizationId || existing.CardId != cardId
                    || existing.UploaderId != actor || existing.RetryKey != retryKey || existing.OriginalCardVersion != proposed.CardVersion
                    || existing.DisplayName != proposed.DisplayName || existing.ExpectedSizeBytes != proposed.ExpectedSizeBytes
                    || existing.ExpectedSha256 != proposed.ExpectedSha256)
                    return WorkOperation<AttachmentUploadRecord>.Failure("idempotency_key_reused");
                if (existing.State == AttachmentUploadState.Abandoned || existing.State != AttachmentUploadState.Published && existing.ExpiresAt <= now)
                    return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable");
                return WorkOperation<AttachmentUploadRecord>.Success(existing);
            }, ct);
    }

    // Call only with an admitted server snapshot. A duplicate never inherits
    // an existing writer nonce and therefore cannot start a second provider I/O.
    public async Task<WorkOperation<AttachmentUploadRecord>> ClaimAsync(Guid cardId, Guid actor,
        Guid uploadId, Guid retryKey, long expectedVersion, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentUploadRecord>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct), async () =>
            {
                var value = await uploads.FindUploadByRetryAsync(hint.OrganizationId, actor, retryKey, ct);
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (value is null || value.Id != uploadId || value.CardId != cardId || value.UploaderId != actor
                    || value.Version != expectedVersion || value.ExpiresAt <= now || value.ExpectedSizeBytes > policy.MaximumBytes)
                    return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable");
                if (value.State != AttachmentUploadState.Prepared) return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_in_progress");
                var current = await work.FindCardAsync(cardId, ct);
                if (current is null || current.Version != value.OriginalCardVersion) return WorkOperation<AttachmentUploadRecord>.Failure("version_conflict");
                var until = now.AddMinutes(10);
                if (until > value.ExpiresAt) until = value.ExpiresAt;
                var claimed = await uploads.TryChangeUploadAsync(hint.OrganizationId, cardId, actor, uploadId, expectedVersion,
                    new(AttachmentUploadAction.StartWrite, now, Guid.NewGuid(), until), ct);
                return claimed is null ? WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable")
                    : WorkOperation<AttachmentUploadRecord>.Success(claimed);
            }, ct);
    }

    public Task<WorkOperation<AttachmentUploadRecord>> RecordStoredAsync(Guid cardId, Guid actor,
        AttachmentUploadRecord writer, AttachmentFileTypeProbe probe, StoredAttachmentObject measured, CancellationToken ct = default)
        => ChangeWriterAsync(cardId, actor, writer, probe, measured, unknown: false, ct);

    public Task<WorkOperation<AttachmentUploadRecord>> MarkUnknownWriteAsync(Guid cardId, Guid actor,
        AttachmentUploadRecord writer, CancellationToken ct = default)
        => ChangeWriterAsync(cardId, actor, writer, null, null, unknown: true, ct);

    public Task<WorkOperation<AttachmentUploadRecord>> ExpireWriterAsync(Guid cardId, Guid actor, AttachmentUploadRecord snapshot, CancellationToken ct)
        => ReconcileAsync(cardId, actor, snapshot, AttachmentUploadAction.ExpiredWriter, null, null, ct);
    // Only a successful private provider lookup proving absence may call this.
    public Task<WorkOperation<AttachmentUploadRecord>> ConfirmMissingAsync(Guid cardId, Guid actor, AttachmentUploadRecord snapshot, CancellationToken ct)
        => ReconcileAsync(cardId, actor, snapshot, AttachmentUploadAction.ConfirmMissing, null, null, ct);
    public Task<WorkOperation<AttachmentUploadRecord>> RecordReconciledAsync(Guid cardId, Guid actor, AttachmentUploadRecord snapshot,
        AttachmentFileTypeProbe probe, StoredAttachmentObject measured, CancellationToken ct)
        => ReconcileAsync(cardId, actor, snapshot, AttachmentUploadAction.RecordReconciled, probe, measured, ct);

    private async Task<WorkOperation<AttachmentUploadRecord>> ReconcileAsync(Guid cardId, Guid actor, AttachmentUploadRecord snapshot,
        AttachmentUploadAction action, AttachmentFileTypeProbe? probe, StoredAttachmentObject? measured, CancellationToken ct)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentUploadRecord>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct), async () =>
            {
                if (snapshot is null || snapshot.OrganizationId != hint.OrganizationId || snapshot.CardId != cardId || snapshot.UploaderId != actor)
                    return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable");
                var value = await uploads.FindUploadByRetryAsync(hint.OrganizationId, actor, snapshot.RetryKey, ct);
                if (value is null || value.Id != snapshot.Id || value.CardId != cardId || value.Version != snapshot.Version || value.State != snapshot.State)
                    return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable");
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (action == AttachmentUploadAction.ExpiredWriter)
                {
                    if (value.State != AttachmentUploadState.Writing || value.WriteLeaseId != snapshot.WriteLeaseId || value.WriteLeaseUntil is null
                        || value.WriteLeaseUntil > now) return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_in_progress");
                }
                else if (value.State != AttachmentUploadState.Reconcile || value.ExpiresAt <= now)
                    return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable");
                if (action == AttachmentUploadAction.RecordReconciled)
                {
                    if (probe is null || measured is null) return WorkOperation<AttachmentUploadRecord>.Failure("attachment_integrity_invalid");
                    try { policy.RequireMeasuredFile(new(hint.OrganizationId, value.Id), probe, measured); }
                    catch (AttachmentUploadValidationException error) { return WorkOperation<AttachmentUploadRecord>.Failure(error.Code); }
                    if (measured.SizeBytes != value.ExpectedSizeBytes || measured.Sha256 != value.ExpectedSha256)
                        return WorkOperation<AttachmentUploadRecord>.Failure("attachment_integrity_invalid");
                }
                var changed = await uploads.TryChangeUploadAsync(hint.OrganizationId, cardId, actor, value.Id, value.Version,
                    new(action, now, Measured: measured, VerifiedMimeType: probe?.MimeType), ct);
                return changed is null ? WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable")
                    : WorkOperation<AttachmentUploadRecord>.Success(changed);
            }, ct);
    }

    // These are server callbacks after provider I/O. Never replace a canonical
    // measurement with caller claims, reset an ambiguous write to Prepared, or
    // roll back an already-committed Stored result after a lost acknowledgment.
    private async Task<WorkOperation<AttachmentUploadRecord>> ChangeWriterAsync(Guid cardId, Guid actor,
        AttachmentUploadRecord writer, AttachmentFileTypeProbe? probe, StoredAttachmentObject? measured, bool unknown, CancellationToken ct)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentUploadRecord>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found",
            () => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct), async () =>
            {
                if (writer is null || writer.OrganizationId != hint.OrganizationId || writer.CardId != cardId || writer.UploaderId != actor
                    || writer.State != AttachmentUploadState.Writing || writer.WriteLeaseId is null || writer.WriteLeaseId == Guid.Empty)
                    return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable");
                var current = await uploads.FindUploadByRetryAsync(hint.OrganizationId, actor, writer.RetryKey, ct);
                if (current is null || current.Id != writer.Id || current.CardId != cardId || current.Version != writer.Version
                    || current.State != AttachmentUploadState.Writing || current.WriteLeaseId != writer.WriteLeaseId)
                    return WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable");
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                AttachmentUploadChange change;
                if (unknown)
                    change = new(AttachmentUploadAction.UnknownWrite, now, writer.WriteLeaseId);
                else
                {
                    if (measured is null || probe is null) return WorkOperation<AttachmentUploadRecord>.Failure("attachment_integrity_invalid");
                    try { policy.RequireMeasuredFile(new(hint.OrganizationId, writer.Id), probe, measured); }
                    catch (AttachmentUploadValidationException error) { return WorkOperation<AttachmentUploadRecord>.Failure(error.Code); }
                    if (measured.SizeBytes != current.ExpectedSizeBytes || measured.Sha256 != current.ExpectedSha256)
                        return WorkOperation<AttachmentUploadRecord>.Failure("attachment_integrity_invalid");
                    change = new(AttachmentUploadAction.RecordStored, now, writer.WriteLeaseId, Measured: measured, VerifiedMimeType: probe.MimeType);
                }
                var changed = await uploads.TryChangeUploadAsync(hint.OrganizationId, cardId, actor, writer.Id, writer.Version, change, ct);
                return changed is null ? WorkOperation<AttachmentUploadRecord>.Failure("attachment_upload_unavailable")
                    : WorkOperation<AttachmentUploadRecord>.Success(changed);
            }, ct);
    }
}
