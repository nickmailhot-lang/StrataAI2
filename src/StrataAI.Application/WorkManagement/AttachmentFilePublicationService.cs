using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// Server command after real admission/storage measurement. No provider I/O is
// allowed inside this transaction and this is not an HTTP upload endpoint.
public sealed class AttachmentFilePublicationService(IWorkManagementStore work, IAttachmentMetadataStore attachments,
    IAttachmentUploadIntentStore uploads, IOrganizationStore organizations, IWorkBoardAuthorization boards,
    IWorkManagementUnitOfWork transactions, ICommandActorAuthorization actors, IClock clock,
    IWorkEventStore events, IAttachmentScanJobPublisher scans, AttachmentUploadPolicy policy)
{
    public async Task<WorkOperation<AttachmentChange>> PublishAsync(Guid cardId, Guid actor,
        Guid uploadId, Guid retryKey, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentChange>.Failure("card_not_found");
        Task<bool> Admit() => AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct);
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, retryKey == Guid.Empty ? null : retryKey, "AttachmentFilePublish", cardId, new { uploadId, retryKey }, "card_not_found"),
            async receipt =>
            {
                if (!await Admit()) return false;
                if (receipt is null) return true;
                var upload = await uploads.FindUploadByRetryAsync(hint.OrganizationId, actor, retryKey, ct);
                var current = await attachments.FindFileAttachmentAsync(hint.OrganizationId, cardId, uploadId, ct);
                return upload is { State: AttachmentUploadState.Published } && upload.Id == uploadId && upload.CardId == cardId
                    && current is not null && current.Metadata.UploaderId == actor
                    && current.Integrity.Sha256 == upload.ExpectedSha256 && current.Integrity.SizeBytes == upload.ExpectedSizeBytes
                    && receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId && receipt.CardId == cardId
                    && receipt.CardVersion == upload.OriginalCardVersion + 1 && receipt.Attachment.Id == uploadId
                    && receipt.Attachment.OrganizationId == hint.OrganizationId && receipt.Attachment.CardId == cardId
                    && receipt.Attachment.UploaderId == actor && receipt.Attachment.Kind == AttachmentKind.File;
            }, async () =>
            {
                if (uploadId == Guid.Empty || retryKey == Guid.Empty) return WorkOperation<AttachmentChange>.Failure("invalid_attachment_upload");
                var upload = await uploads.FindUploadByRetryAsync(hint.OrganizationId, actor, retryKey, ct);
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (upload is null || upload.Id != uploadId || upload.CardId != cardId || upload.UploaderId != actor
                    || upload.State != AttachmentUploadState.Stored || upload.VerifiedMimeType is null || upload.StoredAt is null
                    || upload.StoredAt > now || upload.UpdatedAt > now || upload.ExpiresAt <= now)
                    return WorkOperation<AttachmentChange>.Failure("attachment_upload_unavailable");
                if (upload.ExpectedSizeBytes > policy.MaximumBytes || !policy.AllowedMimeTypes.Contains(upload.VerifiedMimeType))
                    return WorkOperation<AttachmentChange>.Failure("attachment_upload_unavailable");
                var current = await work.FindCardAsync(cardId, ct);
                if (current is null || current.Version != upload.OriginalCardVersion)
                    return WorkOperation<AttachmentChange>.Failure("version_conflict");
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, upload.OriginalCardVersion, now, ct);
                if (updated is null) return WorkOperation<AttachmentChange>.Failure("version_conflict");
                // Stored means these immutable claims were already matched by the
                // trusted byte-measuring storage path, not merely client claims.
                var measured = new StoredAttachmentObject(new(hint.OrganizationId, uploadId), upload.ExpectedSizeBytes, upload.ExpectedSha256);
                var metadata = await attachments.CreateFileAttachmentAsync(measured, cardId, actor, upload.DisplayName, upload.VerifiedMimeType, now, ct);
                var published = await uploads.TryChangeUploadAsync(hint.OrganizationId, cardId, actor, uploadId, upload.Version,
                    new(AttachmentUploadAction.Publish, now), ct);
                var file = await attachments.FindFileAttachmentAsync(hint.OrganizationId, cardId, uploadId, ct);
                if (published is null || file is null || !await scans.PublishScanAsync(published, file, actor, correlationId, ct))
                    return WorkOperation<AttachmentChange>.Failure("work_storage_unavailable");
                await work.AppendAuditAsync(hint.OrganizationId, actor, "ATTACHMENT_ADDED", "Attachment", uploadId, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "ATTACHMENT_ADDED", "Card", cardId,
                    updated.Version, correlationId, now), ct);
                if (!await Admit() || !await actors.VerifyAsync(actor, ct)) return WorkOperation<AttachmentChange>.Failure("session_unavailable");
                return WorkOperation<AttachmentChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, metadata));
            }, ct);
    }
}
