using System.Collections.Concurrent;
using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryAttachmentScanJobPublisher(IAttachmentUploadIntentStore uploads, IAttachmentMetadataStore attachments)
    : IAttachmentScanJobPublisher
{
    private readonly ConcurrentDictionary<(Guid Organization, string Key), NewBackgroundJob> _jobs = new();
    public async Task<bool> PublishScanAsync(AttachmentUploadRecord upload, AttachmentFileRecord file, Guid actor, string correlationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var job = AttachmentScanJobs.Create(upload,file,actor,correlationId);
        if (await uploads.FindUploadByRetryAsync(upload.OrganizationId,upload.UploaderId,upload.RetryKey,ct) != upload
            || await attachments.FindFileAttachmentAsync(upload.OrganizationId,upload.CardId,upload.Id,ct) != file)
            throw new InvalidOperationException("Attachment scan publication is unavailable.");
        ct.ThrowIfCancellationRequested(); return _jobs.TryAdd((job.OrganizationId,job.IdempotencyKey),job);
    }
}

internal sealed class PostgresAttachmentScanJobPublisher(PostgresConnectionFactory connections, PostgresBackgroundJobStore jobs)
    : IAttachmentScanJobPublisher
{
    public async Task<bool> PublishScanAsync(AttachmentUploadRecord upload, AttachmentFileRecord file, Guid actor, string correlationId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (!connections.HasCommandScope(upload.OrganizationId))
            throw new InvalidOperationException("Attachment scan publication requires the owning command transaction.");
        var job = AttachmentScanJobs.Create(upload,file,actor,correlationId);
        await using var session = await connections.OpenTenantSessionAsync(upload.OrganizationId,ct);
        await using (var canonical = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM attachment_upload_intents u JOIN attachments a
             ON a.id=u.published_attachment_id AND a.tenant_id=u.tenant_id AND a.card_id=u.card_id
             WHERE u.id=@id AND u.tenant_id=@tenant AND u.card_id=@card AND u.uploader_id=@actor
              AND u.retry_key=@retry AND u.original_card_version=@card_version AND u.version=@intent_version
              AND u.status='PUBLISHED' AND u.published_at=@published AND u.stored_at=@stored
              AND u.created_at=@intent_created AND u.updated_at=@intent_updated AND u.expires_at=@expires
              AND u.expected_size_bytes=@size AND u.expected_sha256=@digest AND u.verified_mime_type=@mime
              AND u.display_name=@name AND a.uploader_id=@actor AND a.kind='FILE' AND a.version=@file_version
              AND a.created_at=@file_created AND a.updated_at=@file_updated AND a.display_name=@name
              AND a.sha256=@digest AND a.size_bytes=@size AND a.mime_type=@mime AND a.storage_key=@key
              AND a.scan_status='PENDING' AND a.scanned_at IS NULL AND a.deleted_at IS NULL);
            """,session.Connection,session.Transaction))
        {
            canonical.Parameters.AddWithValue("id",upload.Id); canonical.Parameters.AddWithValue("tenant",upload.OrganizationId);
            canonical.Parameters.AddWithValue("card",upload.CardId); canonical.Parameters.AddWithValue("actor",actor);
            canonical.Parameters.AddWithValue("retry",upload.RetryKey); canonical.Parameters.AddWithValue("card_version",upload.OriginalCardVersion);
            canonical.Parameters.AddWithValue("intent_version",upload.Version); canonical.Parameters.AddWithValue("published",upload.PublishedAt!.Value);
            canonical.Parameters.AddWithValue("stored",upload.StoredAt!.Value); canonical.Parameters.AddWithValue("intent_created",upload.CreatedAt);
            canonical.Parameters.AddWithValue("intent_updated",upload.UpdatedAt); canonical.Parameters.AddWithValue("expires",upload.ExpiresAt);
            canonical.Parameters.AddWithValue("size",file.Integrity.SizeBytes); canonical.Parameters.AddWithValue("digest",file.Integrity.Sha256);
            canonical.Parameters.AddWithValue("mime",file.Metadata.MimeType!); canonical.Parameters.AddWithValue("name",file.Metadata.DisplayName);
            canonical.Parameters.AddWithValue("file_version",file.Metadata.Version); canonical.Parameters.AddWithValue("file_created",file.Metadata.CreatedAt);
            canonical.Parameters.AddWithValue("file_updated",file.Metadata.UpdatedAt); canonical.Parameters.AddWithValue("key",file.Integrity.Reference.ObjectKey);
            if (await canonical.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Attachment scan publication is unavailable.");
        }
        // Borrowed transaction: never commit independently of the Card effects.
        return await jobs.PublishAsync(session,job,ct);
    }
}
