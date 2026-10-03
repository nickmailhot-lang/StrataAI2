using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : ICardAttachmentCoverStore
{
    private readonly Dictionary<Guid, Guid> _cardCovers = [];
    public Task<Guid?> FindSelectedAsync(Guid organization, Guid card, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult<Guid?>(_cards.TryGetValue(card, out var parent) && parent.OrganizationId == organization
            && _cardCovers.TryGetValue(card, out var selected) ? selected : null);
    }
    public Task<CardRecord?> SetAsync(Guid organization, Guid board, Guid card, Guid? attachment, long? sourceVersion,
        Guid? previous, long cardVersion, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_cards.TryGetValue(card, out var parent) || parent.OrganizationId != organization || parent.BoardId != board
                || parent.LifecycleState != WorkItemLifecycleState.Active || parent.Version != cardVersion
                || parent.Version == long.MaxValue || now < parent.UpdatedAt
                || (_cardCovers.TryGetValue(card, out var current) ? (Guid?)current : null) != previous)
                return Task.FromResult<CardRecord?>(null);
            if (attachment is { } selected && (!_attachmentMetadata.TryGetValue(selected, out var source)
                || source.OrganizationId != organization || source.CardId != card || source.Version != sourceVersion
                || source.Kind != AttachmentKind.File || source.LifecycleState != AttachmentLifecycleState.Active
                || source.DeletedAt is not null || source.ScanStatus != AttachmentScanStatus.Clean
                || source.MimeType is not ("image/png" or "image/jpeg" or "image/webp"))) return Task.FromResult<CardRecord?>(null);
            var updated = parent with { Version = parent.Version + 1, UpdatedAt = now };
            if (attachment is { } value) _cardCovers[card] = value; else _cardCovers.Remove(card);
            _cards[card] = updated; return Task.FromResult<CardRecord?>(updated);
        }
    }
}

internal sealed partial class PostgresWorkManagementStore : ICardAttachmentCoverStore
{
    public async Task<bool> AcquirePublicReadScopeAsync(Guid organization, Guid board, Guid list, Guid card, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Public cover admission requires the owning read scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        async Task<bool> Lock(string sql)
        {
            await using var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("board", board);
            query.Parameters.AddWithValue("list", list); query.Parameters.AddWithValue("card", card);
            return await query.ExecuteScalarAsync(ct) is Guid;
        }
        return await Lock("SELECT id FROM organizations WHERE id=@tenant AND status='ACTIVE' FOR SHARE;")
            && await Lock("SELECT id FROM boards WHERE tenant_id=@tenant AND id=@board AND visibility='PUBLIC' AND lifecycle_state='ACTIVE' FOR UPDATE;")
            && await Lock("SELECT id FROM board_lists WHERE tenant_id=@tenant AND board_id=@board AND id=@list AND lifecycle_state='ACTIVE' FOR SHARE;")
            && await Lock("SELECT id FROM cards WHERE tenant_id=@tenant AND board_id=@board AND list_id=@list AND id=@card AND lifecycle_state='ACTIVE' FOR SHARE;");
    }
    public async Task<IReadOnlyList<CardCoverCandidate>> ListCandidatesAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        AttachmentMetadataMapping.RequireCursor(beforeCreatedAt, beforeId);
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Cover candidates require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            SELECT a.id,a.version,a.display_name,a.created_at FROM attachments a
            WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.kind='FILE'
             AND a.lifecycle_state='ACTIVE' AND a.deleted_at IS NULL AND a.scan_status='CLEAN' AND a.scanned_at IS NOT NULL
             AND a.version>=3 AND a.mime_type IN ('image/png','image/jpeg','image/webp')
             AND EXISTS (SELECT 1 FROM cards c JOIN board_lists l ON l.tenant_id=c.tenant_id AND l.board_id=c.board_id AND l.id=c.list_id
              JOIN boards b ON b.tenant_id=c.tenant_id AND b.id=c.board_id JOIN organizations o ON o.id=c.tenant_id
              WHERE c.tenant_id=a.tenant_id AND c.id=a.card_id AND c.lifecycle_state='ACTIVE'
               AND l.lifecycle_state='ACTIVE' AND b.lifecycle_state='ACTIVE' AND o.status='ACTIVE')
             AND (@created IS NULL OR (a.created_at,a.id)<(@created,@before))
             AND EXISTS (
              SELECT 1 FROM attachment_previews m JOIN attachment_preview_publications p ON p.id=m.id AND p.tenant_id=m.tenant_id
              WHERE m.tenant_id=a.tenant_id AND m.card_id=a.card_id AND m.attachment_id=a.id AND m.policy_version=1
               AND p.attachment_version=m.source_version+1 AND a.version>=p.attachment_version
               AND m.source_sha256=a.sha256 AND m.source_size_bytes=a.size_bytes AND m.source_mime_type=a.mime_type
               AND m.output_size_bytes BETWEEN 45 AND 8388608 AND m.width BETWEEN 1 AND 1024 AND m.height BETWEEN 1 AND 1024)
            ORDER BY a.created_at DESC,a.id DESC LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("created", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt ?? DBNull.Value);
        query.Parameters.AddWithValue("before", NpgsqlDbType.Uuid, (object?)beforeId ?? DBNull.Value);
        var rows = new List<CardCoverCandidate>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return rows;
    }
    public async Task<Guid?> FindSelectedAsync(Guid organization, Guid card, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Cover reads require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("SELECT cover_attachment_id FROM cards WHERE tenant_id=@tenant AND id=@card;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        return await query.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }
    public async Task<CardRecord?> SetAsync(Guid organization, Guid board, Guid card, Guid? attachment, long? sourceVersion,
        Guid? previous, long cardVersion, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Cover changes require the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var parent = new NpgsqlCommand("""
            SELECT id FROM cards WHERE tenant_id=@tenant AND board_id=@board AND id=@card AND version=@version
             AND version<9223372036854775807 AND updated_at<=@now AND lifecycle_state='ACTIVE'
             AND cover_attachment_id IS NOT DISTINCT FROM @previous FOR UPDATE;
            """, session.Connection, session.Transaction);
        parent.Parameters.AddWithValue("tenant", organization); parent.Parameters.AddWithValue("board", board);
        parent.Parameters.AddWithValue("card", card); parent.Parameters.AddWithValue("version", cardVersion);
        parent.Parameters.AddWithValue("now", now); parent.Parameters.AddWithValue("previous", NpgsqlDbType.Uuid, (object?)previous ?? DBNull.Value);
        if (await parent.ExecuteScalarAsync(ct) is not Guid) return null;
        if (attachment is { } selected)
        {
            if (sourceVersion is not > 0) return null;
            await using var source = new NpgsqlCommand("""
                SELECT id FROM attachments WHERE tenant_id=@tenant AND card_id=@card AND id=@file AND version=@version
                 AND kind='FILE' AND lifecycle_state='ACTIVE' AND deleted_at IS NULL AND scan_status='CLEAN'
                 AND mime_type IN ('image/png','image/jpeg','image/webp') FOR UPDATE;
                """, session.Connection, session.Transaction);
            source.Parameters.AddWithValue("tenant", organization); source.Parameters.AddWithValue("card", card);
            source.Parameters.AddWithValue("file", selected); source.Parameters.AddWithValue("version", sourceVersion.Value);
            if (await source.ExecuteScalarAsync(ct) is not Guid) return null;
        }
        await using var update = new NpgsqlCommand("""
            UPDATE cards SET cover_attachment_id=@file,version=version+1,updated_at=@now
            WHERE tenant_id=@tenant AND board_id=@board AND id=@card AND version=@version
             AND cover_attachment_id IS NOT DISTINCT FROM @previous AND lifecycle_state='ACTIVE'
            RETURNING id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,created_at,updated_at,version,
             start_at,due_at,due_timezone,due_has_time,due_complete;
            """, session.Connection, session.Transaction);
        update.Parameters.AddWithValue("tenant", organization); update.Parameters.AddWithValue("board", board); update.Parameters.AddWithValue("card", card);
        update.Parameters.AddWithValue("version", cardVersion); update.Parameters.AddWithValue("now", now);
        update.Parameters.AddWithValue("file", NpgsqlDbType.Uuid, (object?)attachment ?? DBNull.Value);
        update.Parameters.AddWithValue("previous", NpgsqlDbType.Uuid, (object?)previous ?? DBNull.Value);
        await using var row = await update.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? ReadCard(row) : null;
    }
}
