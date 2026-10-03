using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IAttachmentMetadataStore
{
    private readonly Dictionary<Guid, AttachmentMetadata> _attachmentMetadata = [];

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
            && value.OrganizationId == organization && value.CardId == card && value.DeletedAt is null ? value : null);
    }
    public Task<IReadOnlyList<AttachmentMetadata>> ListAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        AttachmentMetadataMapping.RequireCursor(beforeCreatedAt, beforeId);
        lock (_sync) return Task.FromResult<IReadOnlyList<AttachmentMetadata>>(_attachmentMetadata.Values
            .Where(value => value.OrganizationId == organization && value.CardId == card && value.DeletedAt is null
                && (beforeCreatedAt is null || value.CreatedAt < beforeCreatedAt || value.CreatedAt == beforeCreatedAt && value.Id.CompareTo(beforeId!.Value) < 0))
            .OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id).Take(51).ToArray());
    }
}

internal sealed partial class PostgresWorkManagementStore : IAttachmentMetadataStore
{
    private const string AttachmentMetadataColumns = "a.id,a.tenant_id,a.card_id,a.uploader_id,a.kind,a.display_name,a.mime_type,a.size_bytes,a.url,a.scan_status,a.scanned_at,a.created_at,a.updated_at,a.version,a.deleted_at";
    private static AttachmentMetadata ReadAttachmentMetadata(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetGuid(3),
        row.GetString(4) switch { "FILE" => AttachmentKind.File, "URL" => AttachmentKind.Url, _ => throw new InvalidOperationException("Attachment kind metadata is invalid.") }, row.GetString(5), row.IsDBNull(6) ? null : row.GetString(6),
        row.IsDBNull(7) ? null : row.GetInt64(7), row.IsDBNull(8) ? null : row.GetString(8), row.GetString(9) switch
        {
            "NOT_APPLICABLE" => AttachmentScanStatus.NotApplicable, "PENDING" => AttachmentScanStatus.Pending,
            "CLEAN" => AttachmentScanStatus.Clean, "REJECTED" => AttachmentScanStatus.Rejected, "FAILED" => AttachmentScanStatus.Failed,
            _ => throw new InvalidOperationException("Attachment scan metadata is invalid.")
        }, row.IsDBNull(10) ? null : row.GetFieldValue<DateTimeOffset>(10), row.GetFieldValue<DateTimeOffset>(11),
        row.GetFieldValue<DateTimeOffset>(12), row.GetInt64(13), row.IsDBNull(14) ? null : row.GetFieldValue<DateTimeOffset>(14));

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
        await using var query = new NpgsqlCommand($"SELECT {AttachmentMetadataColumns} FROM attachments a WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.id=@id AND a.deleted_at IS NULL;", session.Connection, session.Transaction);
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
            WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.deleted_at IS NULL
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
