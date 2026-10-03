using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// Expected digest binds the original request; it is not verified content.
public sealed record PrepareAttachmentUploadInput(string DisplayName, long SizeBytes, string Sha256, long CardVersion);

public sealed class AttachmentUploadAdmissionService(IWorkManagementStore work, IOrganizationStore organizations,
    IWorkBoardAuthorization boards, IAttachmentUploadIntentStore uploads, IWorkManagementUnitOfWork transactions,
    IClock clock, AttachmentUploadPolicy policy)
{
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
}
