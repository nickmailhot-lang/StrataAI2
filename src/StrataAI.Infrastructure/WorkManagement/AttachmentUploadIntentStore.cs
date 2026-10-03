using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IAttachmentUploadIntentStore
{
    private readonly Dictionary<Guid, AttachmentUploadIntent> _uploads = [];
    public async Task<AttachmentUploadRecord?> PrepareUploadAsync(AttachmentUploadIntent prepared, CancellationToken ct)
    {
        AttachmentUploadPersistence.RequirePrepared(prepared); ct.ThrowIfCancellationRequested();
        if (await organizations.FindMembershipAsync(prepared.OrganizationId, prepared.UploaderId, ct) is null)
            throw new InvalidOperationException("Upload uploader unavailable.");
        var copy = AttachmentUploadIntent.Prepare(prepared.Id, prepared.OrganizationId, prepared.CardId, prepared.UploaderId,
            prepared.RetryKey, prepared.CardVersion, prepared.DisplayName, prepared.ExpectedSizeBytes, prepared.ExpectedSha256,
            AttachmentMetadataMapping.DatabaseTimestamp(prepared.ExpiresAt), AttachmentMetadataMapping.DatabaseTimestamp(prepared.CreatedAt));
        lock (_sync)
        {
            if (!_cards.TryGetValue(copy.CardId, out var parent) || parent.OrganizationId != copy.OrganizationId)
                throw new InvalidOperationException("Upload parent unavailable.");
            if (_uploads.ContainsKey(copy.Id) || _uploads.Values.Any(x => x.OrganizationId == copy.OrganizationId
                && x.UploaderId == copy.UploaderId && x.RetryKey == copy.RetryKey)) return null;
            _uploads.Add(copy.Id, copy); return AttachmentUploadRecord.From(copy);
        }
    }
    public Task<AttachmentUploadRecord?> FindUploadByRetryAsync(Guid organization, Guid uploader, Guid retryKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var value = _uploads.Values.SingleOrDefault(x => x.OrganizationId == organization && x.UploaderId == uploader && x.RetryKey == retryKey);
            return Task.FromResult(value is null ? null : AttachmentUploadRecord.From(value));
        }
    }
    public Task<AttachmentUploadRecord?> TryChangeUploadAsync(Guid organization, Guid card, Guid uploader, Guid attachment,
        long expectedVersion, AttachmentUploadChange change, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); change.Validate(organization, attachment);
        var at = AttachmentMetadataMapping.DatabaseTimestamp(change.At);
        lock (_sync)
        {
            if (!_uploads.TryGetValue(attachment, out var value) || value.OrganizationId != organization || value.CardId != card
                || value.UploaderId != uploader || value.Version != expectedVersion || at < value.UpdatedAt) return Task.FromResult<AttachmentUploadRecord?>(null);
            if (change.Action == AttachmentUploadAction.Publish && (!_attachmentMetadata.TryGetValue(attachment, out var metadata)
                || metadata.OrganizationId != organization || metadata.CardId != card || metadata.UploaderId != uploader
                || metadata.Kind != AttachmentKind.File || metadata.DeletedAt is not null || metadata.ScanStatus != AttachmentScanStatus.Pending
                || metadata.DisplayName != value.DisplayName || metadata.MimeType != value.VerifiedMimeType
                || metadata.SizeBytes != value.ExpectedSizeBytes || !_attachmentIntegrity.TryGetValue(attachment, out var integrity)
                || integrity.Sha256 != value.ExpectedSha256)) return Task.FromResult<AttachmentUploadRecord?>(null);
            try
            {
                switch (change.Action)
                {
                    case AttachmentUploadAction.StartWrite: value.StartWrite(change.LeaseId!.Value, AttachmentMetadataMapping.DatabaseTimestamp(change.LeaseUntil!.Value), at); break;
                    case AttachmentUploadAction.RenewWrite: if (!value.RenewWrite(change.LeaseId!.Value, AttachmentMetadataMapping.DatabaseTimestamp(change.LeaseUntil!.Value), at)) return Task.FromResult<AttachmentUploadRecord?>(null); break;
                    case AttachmentUploadAction.UnknownWrite: value.RecordUnknownWrite(change.LeaseId!.Value, at); break;
                    case AttachmentUploadAction.ExpiredWriter: value.ReconcileExpiredWriter(at); break;
                    case AttachmentUploadAction.ConfirmMissing: value.ConfirmMissingObject(at); break;
                    case AttachmentUploadAction.RecordStored: value.RecordStored(change.LeaseId!.Value, change.VerifiedMimeType!, change.Measured!.SizeBytes, change.Measured.Sha256, at); break;
                    case AttachmentUploadAction.RecordReconciled: value.RecordReconciledObject(change.VerifiedMimeType!, change.Measured!.SizeBytes, change.Measured.Sha256, at); break;
                    case AttachmentUploadAction.Abandon: if (!value.Abandon(at)) return Task.FromResult<AttachmentUploadRecord?>(null); break;
                    case AttachmentUploadAction.Publish: if (!value.Publish(at)) return Task.FromResult<AttachmentUploadRecord?>(null); break;
                    default: throw new ArgumentOutOfRangeException(nameof(change));
                }
                return Task.FromResult<AttachmentUploadRecord?>(AttachmentUploadRecord.From(value));
            }
            catch (InvalidOperationException) { return Task.FromResult<AttachmentUploadRecord?>(null); }
        }
    }
}

internal sealed partial class PostgresWorkManagementStore : IAttachmentUploadIntentStore
{
    private const string UploadColumns = "id,tenant_id,card_id,uploader_id,retry_key,original_card_version,display_name,expected_size_bytes,expected_sha256,created_at,updated_at,expires_at,version,status,write_lease_id,write_lease_until,verified_mime_type,stored_at,published_at,abandoned_at";
    private static AttachmentUploadRecord ReadUpload(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetGuid(3),
        row.GetGuid(4), row.GetInt64(5), row.GetString(6), row.GetInt64(7), row.GetString(8), row.GetFieldValue<DateTimeOffset>(9),
        row.GetFieldValue<DateTimeOffset>(10), row.GetFieldValue<DateTimeOffset>(11), row.GetInt64(12), row.GetString(13) switch
        {
            "PREPARED" => AttachmentUploadState.Prepared, "WRITING" => AttachmentUploadState.Writing,
            "RECONCILE" => AttachmentUploadState.Reconcile, "STORED" => AttachmentUploadState.Stored,
            "PUBLISHED" => AttachmentUploadState.Published, "ABANDONED" => AttachmentUploadState.Abandoned,
            _ => throw new InvalidOperationException("Upload state is invalid.")
        }, row.IsDBNull(14) ? null : row.GetGuid(14), row.IsDBNull(15) ? null : row.GetFieldValue<DateTimeOffset>(15),
        row.IsDBNull(16) ? null : row.GetString(16), row.IsDBNull(17) ? null : row.GetFieldValue<DateTimeOffset>(17),
        row.IsDBNull(18) ? null : row.GetFieldValue<DateTimeOffset>(18), row.IsDBNull(19) ? null : row.GetFieldValue<DateTimeOffset>(19));
    private void RequireUploadScope(Guid organization)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Upload persistence requires the owning command transaction.");
    }
    public async Task<AttachmentUploadRecord?> PrepareUploadAsync(AttachmentUploadIntent prepared, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prepared); RequireUploadScope(prepared.OrganizationId);
        AttachmentUploadPersistence.RequirePrepared(prepared);
        await using var session = await connectionFactory.OpenTenantSessionAsync(prepared.OrganizationId, ct);
        await using var query = new NpgsqlCommand($"""
            INSERT INTO attachment_upload_intents(id,tenant_id,card_id,uploader_id,retry_key,original_card_version,display_name,expected_size_bytes,expected_sha256,created_at,updated_at,expires_at)
            VALUES(@id,@tenant,@card,@uploader,@retry,@revision,@name,@size,@digest,@at,@at,@expires)
            ON CONFLICT DO NOTHING RETURNING {UploadColumns};
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("id", prepared.Id); query.Parameters.AddWithValue("tenant", prepared.OrganizationId);
        query.Parameters.AddWithValue("card", prepared.CardId); query.Parameters.AddWithValue("uploader", prepared.UploaderId);
        query.Parameters.AddWithValue("retry", prepared.RetryKey); query.Parameters.AddWithValue("revision", prepared.CardVersion);
        query.Parameters.AddWithValue("name", prepared.DisplayName); query.Parameters.AddWithValue("size", prepared.ExpectedSizeBytes);
        query.Parameters.AddWithValue("digest", prepared.ExpectedSha256);
        query.Parameters.AddWithValue("at", AttachmentMetadataMapping.DatabaseTimestamp(prepared.CreatedAt));
        query.Parameters.AddWithValue("expires", AttachmentMetadataMapping.DatabaseTimestamp(prepared.ExpiresAt));
        await using var reader = await query.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadUpload(reader) : null;
    }
    public async Task<AttachmentUploadRecord?> FindUploadByRetryAsync(Guid organization, Guid uploader, Guid retryKey, CancellationToken ct)
    {
        RequireUploadScope(organization);
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"SELECT {UploadColumns} FROM attachment_upload_intents WHERE tenant_id=@tenant AND uploader_id=@uploader AND retry_key=@retry;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("uploader", uploader); query.Parameters.AddWithValue("retry", retryKey);
        await using var reader = await query.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadUpload(reader) : null;
    }
    public async Task<AttachmentUploadRecord?> TryChangeUploadAsync(Guid organization, Guid card, Guid uploader, Guid attachment,
        long expectedVersion, AttachmentUploadChange change, CancellationToken ct)
    {
        RequireUploadScope(organization); change.Validate(organization, attachment);
        var (set, predicate) = change.Action switch
        {
            AttachmentUploadAction.StartWrite => ("status='WRITING',write_lease_id=@lease,write_lease_until=@until", "status='PREPARED' AND @until<=expires_at"),
            AttachmentUploadAction.RenewWrite => ("write_lease_until=@until", "status='WRITING' AND write_lease_id=@lease AND @at<write_lease_until AND @until>write_lease_until AND @until<=expires_at"),
            AttachmentUploadAction.UnknownWrite => ("status='RECONCILE',write_lease_id=NULL,write_lease_until=NULL", "status='WRITING' AND write_lease_id=@lease"),
            AttachmentUploadAction.ExpiredWriter => ("status='RECONCILE',write_lease_id=NULL,write_lease_until=NULL", "status='WRITING' AND @at>=write_lease_until"),
            AttachmentUploadAction.ConfirmMissing => ("status='PREPARED'", "status='RECONCILE' AND @at<expires_at"),
            AttachmentUploadAction.RecordStored => ("status='STORED',write_lease_id=NULL,write_lease_until=NULL,verified_mime_type=@mime,stored_at=@at", "status='WRITING' AND write_lease_id=@lease AND @at<write_lease_until AND @at<expires_at AND expected_size_bytes=@size AND expected_sha256=@digest"),
            AttachmentUploadAction.RecordReconciled => ("status='STORED',verified_mime_type=@mime,stored_at=@at", "status='RECONCILE' AND @at<expires_at AND expected_size_bytes=@size AND expected_sha256=@digest"),
            AttachmentUploadAction.Abandon => ("status='ABANDONED',abandoned_at=@at", "status IN ('PREPARED','RECONCILE','STORED')"),
            AttachmentUploadAction.Publish => ("status='PUBLISHED',published_attachment_id=id,published_at=@at", """
                status='STORED' AND @at<expires_at AND EXISTS(SELECT 1 FROM attachments a
                WHERE a.id=u.id AND a.tenant_id=u.tenant_id AND a.card_id=u.card_id AND a.uploader_id=u.uploader_id
                 AND a.kind='FILE' AND a.display_name=u.display_name AND a.mime_type=u.verified_mime_type
                 AND a.size_bytes=u.expected_size_bytes AND a.sha256=u.expected_sha256
                 AND a.storage_key='attachments/'||replace(u.tenant_id::text,'-','')||'/'||replace(u.id::text,'-','')
                 AND a.scan_status='PENDING' AND a.deleted_at IS NULL)
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        // SQL fragments originate exclusively from the closed server enum.
        await using var query = new NpgsqlCommand($"""
            UPDATE attachment_upload_intents u SET {set},updated_at=@at,version=version+1
            WHERE tenant_id=@tenant AND card_id=@card AND uploader_id=@uploader AND id=@id
             AND version=@version AND updated_at<=@at AND ({predicate}) RETURNING {UploadColumns};
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("uploader", uploader); query.Parameters.AddWithValue("id", attachment);
        query.Parameters.AddWithValue("version", expectedVersion); query.Parameters.AddWithValue("at", AttachmentMetadataMapping.DatabaseTimestamp(change.At));
        query.Parameters.AddWithValue("lease", NpgsqlDbType.Uuid, (object?)change.LeaseId ?? DBNull.Value);
        query.Parameters.AddWithValue("until", NpgsqlDbType.TimestampTz, change.LeaseUntil.HasValue ? AttachmentMetadataMapping.DatabaseTimestamp(change.LeaseUntil.Value) : DBNull.Value);
        query.Parameters.AddWithValue("mime", NpgsqlDbType.Text, (object?)change.VerifiedMimeType ?? DBNull.Value);
        query.Parameters.AddWithValue("size", NpgsqlDbType.Bigint, (object?)change.Measured?.SizeBytes ?? DBNull.Value);
        query.Parameters.AddWithValue("digest", NpgsqlDbType.Text, (object?)change.Measured?.Sha256 ?? DBNull.Value);
        await using var reader = await query.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadUpload(reader) : null;
    }
}

internal static class AttachmentUploadPersistence
{
    internal static void RequirePrepared(AttachmentUploadIntent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.State != AttachmentUploadState.Prepared || value.Version != 1 || value.UpdatedAt != value.CreatedAt)
            throw new ArgumentException("Upload persistence requires a new prepared intent.");
        if (AttachmentMetadataMapping.DatabaseTimestamp(value.ExpiresAt) <= AttachmentMetadataMapping.DatabaseTimestamp(value.CreatedAt))
            throw new ArgumentException("Upload expiry must fit database timestamp precision.");
    }
}
