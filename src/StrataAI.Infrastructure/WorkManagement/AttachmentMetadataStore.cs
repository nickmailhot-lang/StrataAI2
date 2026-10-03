using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IAttachmentMetadataStore
{
    private readonly Dictionary<Guid, AttachmentMetadata> _attachmentMetadata = [];
    private readonly Dictionary<Guid, AttachmentScanRequest> _attachmentIntegrity = [];

    public async Task<AttachmentMetadata> CreateFileAttachmentAsync(StoredAttachmentObject measured, Guid card, Guid uploader,
        string displayName, string verifiedMimeType, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(measured); ArgumentNullException.ThrowIfNull(measured.Reference); ct.ThrowIfCancellationRequested();
        var reference = measured.Reference;
        if (await organizations.FindMembershipAsync(reference.OrganizationId, uploader, ct) is null)
            throw new InvalidOperationException("Attachment uploader unavailable.");
        var value = Attachment.QuarantineFile(reference.AttachmentId, reference.OrganizationId, card, uploader, displayName,
            verifiedMimeType, measured.SizeBytes, reference.ObjectKey, measured.Sha256, AttachmentMetadataMapping.DatabaseTimestamp(now));
        var metadata = AttachmentMetadataMapping.From(value); var integrity = new AttachmentScanRequest(reference, measured.SizeBytes, measured.Sha256);
        lock (_sync)
        {
            if (!_cards.TryGetValue(card, out var parent) || parent.OrganizationId != reference.OrganizationId)
                throw new InvalidOperationException("Attachment parent unavailable.");
            _attachmentMetadata.Add(value.Id, metadata); _attachmentIntegrity.Add(value.Id, integrity); return metadata;
        }
    }
    public Task<AttachmentFileRecord?> FindFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
        => FindFileAsync(organization, card, attachment, AttachmentLifecycleState.Active, ct);
    public Task<AttachmentFileRecord?> FindArchivedFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
        => FindFileAsync(organization, card, attachment, AttachmentLifecycleState.Archived, ct);
    private Task<AttachmentFileRecord?> FindFileAsync(Guid organization, Guid card, Guid attachment, AttachmentLifecycleState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(_attachmentMetadata.TryGetValue(attachment, out var value)
            && value.OrganizationId == organization && value.CardId == card && value.Kind == AttachmentKind.File && value.DeletedAt is null && value.LifecycleState == state
            && _attachmentIntegrity.TryGetValue(attachment, out var integrity) ? new AttachmentFileRecord(value, integrity) : null);
    }

    public async Task<AttachmentMetadata> CreateUrlAttachmentAsync(Guid id, Guid organization, Guid card, Guid uploader,
        string title, string url, DateTimeOffset now, CancellationToken ct)
    {
        if (await organizations.FindMembershipAsync(organization, uploader, ct) is null)
            throw new InvalidOperationException("Attachment uploader unavailable.");
        var value = AttachmentMetadataMapping.From(Attachment.AttachUrl(id, organization, card, uploader, title, url,
            AttachmentMetadataMapping.DatabaseTimestamp(now)));
        lock (_sync)
        {
            if (!_cards.TryGetValue(card, out var parent) || parent.OrganizationId != organization)
                throw new InvalidOperationException("Attachment parent unavailable.");
            _attachmentMetadata.Add(id, value); return value;
        }
    }
    public Task<AttachmentMetadata?> FindAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult(_attachmentMetadata.TryGetValue(attachment, out var value)
            && value.OrganizationId == organization && value.CardId == card && value.DeletedAt is null && value.LifecycleState == AttachmentLifecycleState.Active ? value : null);
    }
    public Task<IReadOnlyList<AttachmentMetadata>> ListAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        AttachmentMetadataMapping.RequireCursor(beforeCreatedAt, beforeId);
        lock (_sync) return Task.FromResult<IReadOnlyList<AttachmentMetadata>>(_attachmentMetadata.Values
            .Where(value => value.OrganizationId == organization && value.CardId == card && value.DeletedAt is null && value.LifecycleState == AttachmentLifecycleState.Active
                && (beforeCreatedAt is null || value.CreatedAt < beforeCreatedAt || value.CreatedAt == beforeCreatedAt && value.Id.CompareTo(beforeId!.Value) < 0))
            .OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id).Take(51).ToArray());
    }
}

internal sealed partial class PostgresWorkManagementStore : IAttachmentMetadataStore
{
    public async Task<AttachmentPublishedPreview?> FindPublishedPreviewAsync(AttachmentFileRecord source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var file = source.Metadata;
        if (!connectionFactory.HasCommandScope(file.OrganizationId))
            throw new InvalidOperationException("Preview reads require the owning scope.");
        if (source.Integrity.Reference.IsPreview || source.Integrity.Reference.OrganizationId != file.OrganizationId
            || source.Integrity.Reference.AttachmentId != file.Id
            || file.LifecycleState is not (AttachmentLifecycleState.Active or AttachmentLifecycleState.Archived)) return null;
        await using var session = await connectionFactory.OpenTenantSessionAsync(file.OrganizationId, ct);
        await using var query = new NpgsqlCommand("""
            SELECT m.id,m.output_size_bytes,m.output_sha256,m.width,m.height
            FROM attachment_previews m
            JOIN attachment_preview_publications p ON p.id=m.id AND p.tenant_id=m.tenant_id
            JOIN attachments a ON a.id=m.attachment_id AND a.tenant_id=m.tenant_id AND a.card_id=m.card_id
            WHERE m.tenant_id=@tenant AND m.card_id=@card AND m.attachment_id=@file AND m.policy_version=1
              AND p.attachment_version=m.source_version+1 AND a.version>=p.attachment_version AND a.version=@version
              AND a.kind='FILE' AND a.scan_status='CLEAN' AND a.deleted_at IS NULL AND a.lifecycle_state=@state
              AND a.storage_key=@key AND a.sha256=@digest AND a.size_bytes=@size AND a.mime_type=@mime
              AND m.source_sha256=a.sha256 AND m.source_size_bytes=a.size_bytes AND m.source_mime_type=a.mime_type
            ORDER BY m.source_version DESC LIMIT 1;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", file.OrganizationId); query.Parameters.AddWithValue("card", file.CardId);
        query.Parameters.AddWithValue("file", file.Id); query.Parameters.AddWithValue("version", file.Version);
        query.Parameters.AddWithValue("state", file.LifecycleState == AttachmentLifecycleState.Archived ? "ARCHIVED" : "ACTIVE");
        query.Parameters.AddWithValue("key", source.Integrity.Reference.ObjectKey); query.Parameters.AddWithValue("digest", source.Integrity.Sha256);
        query.Parameters.AddWithValue("size", source.Integrity.SizeBytes); query.Parameters.AddWithValue("mime", file.MimeType ?? "");
        await using var row = await query.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct)
            ? new(new(AttachmentObjectReference.ForPreview(file.OrganizationId, row.GetGuid(0)), row.GetInt64(1), row.GetString(2)),
                row.GetInt32(3), row.GetInt32(4)) : null;
    }
    private const string AttachmentMetadataColumns = "a.id,a.tenant_id,a.card_id,a.uploader_id,a.kind,a.display_name,a.mime_type,a.size_bytes,a.url,a.scan_status,a.scanned_at,a.created_at,a.updated_at,a.version,a.deleted_at,a.lifecycle_state,a.archived_at,a.deleted_by";
    private static AttachmentMetadata ReadAttachmentMetadata(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetGuid(3),
        row.GetString(4) switch { "FILE" => AttachmentKind.File, "URL" => AttachmentKind.Url, _ => throw new InvalidOperationException("Attachment kind metadata is invalid.") }, row.GetString(5), row.IsDBNull(6) ? null : row.GetString(6),
        row.IsDBNull(7) ? null : row.GetInt64(7), row.IsDBNull(8) ? null : row.GetString(8), row.GetString(9) switch
        {
            "NOT_APPLICABLE" => AttachmentScanStatus.NotApplicable, "PENDING" => AttachmentScanStatus.Pending,
            "CLEAN" => AttachmentScanStatus.Clean, "REJECTED" => AttachmentScanStatus.Rejected, "FAILED" => AttachmentScanStatus.Failed,
            _ => throw new InvalidOperationException("Attachment scan metadata is invalid.")
        }, row.IsDBNull(10) ? null : row.GetFieldValue<DateTimeOffset>(10), row.GetFieldValue<DateTimeOffset>(11),
        row.GetFieldValue<DateTimeOffset>(12), row.GetInt64(13), row.IsDBNull(14) ? null : row.GetFieldValue<DateTimeOffset>(14))
        { LifecycleState = row.GetString(15) switch { "ACTIVE" => AttachmentLifecycleState.Active, "ARCHIVED" => AttachmentLifecycleState.Archived,
            "DELETED" => AttachmentLifecycleState.Deleted, _ => throw new InvalidOperationException("Attachment lifecycle metadata is invalid.") },
          ArchivedAt = row.IsDBNull(16) ? null : row.GetFieldValue<DateTimeOffset>(16), DeletedBy = row.IsDBNull(17) ? null : row.GetGuid(17) };

    public async Task<AttachmentMetadata> CreateFileAttachmentAsync(StoredAttachmentObject measured, Guid card, Guid uploader,
        string displayName, string verifiedMimeType, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(measured); ArgumentNullException.ThrowIfNull(measured.Reference);
        var reference = measured.Reference;
        if (!connectionFactory.HasCommandScope(reference.OrganizationId)) throw new InvalidOperationException("Attachment writes require the owning command transaction.");
        var value = Attachment.QuarantineFile(reference.AttachmentId, reference.OrganizationId, card, uploader, displayName,
            verifiedMimeType, measured.SizeBytes, reference.ObjectKey, measured.Sha256, AttachmentMetadataMapping.DatabaseTimestamp(now));
        await using var session = await connectionFactory.OpenTenantSessionAsync(reference.OrganizationId, ct);
        await using var query = new NpgsqlCommand($"""
            INSERT INTO attachments AS a(id,tenant_id,card_id,uploader_id,kind,display_name,mime_type,size_bytes,storage_key,sha256,scan_status,created_at,updated_at)
            VALUES(@id,@tenant,@card,@uploader,'FILE',@title,@mime,@size,@key,@digest,'PENDING',@now,@now)
            RETURNING {AttachmentMetadataColumns};
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("id", value.Id); query.Parameters.AddWithValue("tenant", value.OrganizationId);
        query.Parameters.AddWithValue("card", value.CardId); query.Parameters.AddWithValue("uploader", value.UploaderId);
        query.Parameters.AddWithValue("title", value.DisplayName); query.Parameters.AddWithValue("mime", value.MimeType!);
        query.Parameters.AddWithValue("size", value.SizeBytes!.Value); query.Parameters.AddWithValue("key", reference.ObjectKey);
        query.Parameters.AddWithValue("digest", value.Sha256!); query.Parameters.AddWithValue("now", value.CreatedAt);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Attachment creation returned no metadata.");
        return ReadAttachmentMetadata(reader);
    }
    public async Task<AttachmentFileRecord?> FindFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
        => await FindFileAsync(organization, card, attachment, "ACTIVE", ct);
    public async Task<AttachmentFileRecord?> FindArchivedFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
        => await FindFileAsync(organization, card, attachment, "ARCHIVED", ct);
    private async Task<AttachmentFileRecord?> FindFileAsync(Guid organization, Guid card, Guid attachment, string state, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Attachment reads require the owning scope.");
        var reference = new AttachmentObjectReference(organization, attachment);
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"""
            SELECT {AttachmentMetadataColumns},a.sha256 FROM attachments a
            WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.id=@id AND a.kind='FILE'
              AND a.deleted_at IS NULL AND a.lifecycle_state=@state AND a.sha256 IS NOT NULL AND a.storage_key=@key;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("id", attachment); query.Parameters.AddWithValue("key", reference.ObjectKey);
        query.Parameters.AddWithValue("state", state);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var metadata = ReadAttachmentMetadata(reader);
        return new(metadata, new(reference, metadata.SizeBytes!.Value, reader.GetString(18)));
    }

    public async Task<AttachmentMetadata> CreateUrlAttachmentAsync(Guid id, Guid organization, Guid card, Guid uploader,
        string title, string url, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Attachment writes require the owning command transaction.");
        var value = Attachment.AttachUrl(id, organization, card, uploader, title, url, AttachmentMetadataMapping.DatabaseTimestamp(now));
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"""
            INSERT INTO attachments AS a(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
            VALUES(@id,@tenant,@card,@uploader,'URL',@title,@url,'NOT_APPLICABLE',@now,@now)
            RETURNING {AttachmentMetadataColumns};
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("id", value.Id); query.Parameters.AddWithValue("tenant", value.OrganizationId);
        query.Parameters.AddWithValue("card", value.CardId); query.Parameters.AddWithValue("uploader", value.UploaderId);
        query.Parameters.AddWithValue("title", value.DisplayName); query.Parameters.AddWithValue("url", value.Url!);
        query.Parameters.AddWithValue("now", value.CreatedAt);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Attachment creation returned no metadata.");
        return ReadAttachmentMetadata(reader);
    }
    public async Task<AttachmentMetadata?> FindAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Attachment reads require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"SELECT {AttachmentMetadataColumns} FROM attachments a WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.id=@id AND a.deleted_at IS NULL AND a.lifecycle_state='ACTIVE';", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("id", attachment);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAttachmentMetadata(reader) : null;
    }
    public async Task<IReadOnlyList<AttachmentMetadata>> ListAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Attachment reads require the owning scope.");
        AttachmentMetadataMapping.RequireCursor(beforeCreatedAt, beforeId);
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"""
            SELECT {AttachmentMetadataColumns} FROM attachments a
            WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.deleted_at IS NULL AND a.lifecycle_state='ACTIVE'
              AND (@created IS NULL OR (a.created_at,a.id)<(@created,@id))
            ORDER BY a.created_at DESC,a.id DESC LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("created", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt?.ToUniversalTime() ?? DBNull.Value);
        query.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)beforeId ?? DBNull.Value);
        var rows = new List<AttachmentMetadata>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadAttachmentMetadata(reader));
        return rows;
    }
}
