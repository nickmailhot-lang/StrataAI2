using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<AttachmentMetadata?> FindLifecycleAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(_attachmentMetadata.TryGetValue(attachment, out var value)
            && value.OrganizationId == organization && value.CardId == card ? value : null);
    }
    public Task<IReadOnlyList<AttachmentMetadata>> ListArchivedAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); AttachmentMetadataMapping.RequireCursor(beforeCreatedAt, beforeId);
        lock (_sync) return Task.FromResult<IReadOnlyList<AttachmentMetadata>>(_attachmentMetadata.Values
            .Where(value => value.OrganizationId == organization && value.CardId == card
                && value.LifecycleState == AttachmentLifecycleState.Archived && value.DeletedAt is null
                && (beforeCreatedAt is null || value.CreatedAt < beforeCreatedAt || value.CreatedAt == beforeCreatedAt && value.Id.CompareTo(beforeId!.Value) < 0))
            .OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id).Take(51).ToArray());
    }
    public Task<AttachmentMetadata?> ChangeAttachmentLifecycleAsync(Guid organization, Guid card, Guid attachment,
        long version, AttachmentLifecycleState from, AttachmentLifecycleState to, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); AttachmentLifecycleTransition.Require(from, to, actor);
        now = AttachmentMetadataMapping.DatabaseTimestamp(now);
        lock (_sync)
        {
            if (!_attachmentMetadata.TryGetValue(attachment, out var value) || value.OrganizationId != organization || value.CardId != card
                || value.Version != version || value.LifecycleState != from || version == long.MaxValue || now < value.UpdatedAt)
                return Task.FromResult<AttachmentMetadata?>(null);
            var changed = value with { LifecycleState = to, Version = version + 1, UpdatedAt = now,
                ArchivedAt = to == AttachmentLifecycleState.Archived ? now : value.ArchivedAt,
                DeletedAt = to == AttachmentLifecycleState.Deleted ? now : null,
                DeletedBy = to == AttachmentLifecycleState.Deleted ? actor : null };
            _attachmentMetadata[attachment] = changed; return Task.FromResult<AttachmentMetadata?>(changed);
        }
    }
}

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<AttachmentMetadata?> FindLifecycleAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Lifecycle reads require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"SELECT {AttachmentMetadataColumns} FROM attachments a WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.id=@id;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("id", attachment);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAttachmentMetadata(reader) : null;
    }
    public async Task<IReadOnlyList<AttachmentMetadata>> ListArchivedAttachmentsAsync(Guid organization, Guid card,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Archive reads require the owning scope.");
        AttachmentMetadataMapping.RequireCursor(beforeCreatedAt, beforeId);
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"""
            SELECT {AttachmentMetadataColumns} FROM attachments a
            WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.lifecycle_state='ARCHIVED' AND a.deleted_at IS NULL
              AND (@created IS NULL OR (a.created_at,a.id)<(@created,@id))
            ORDER BY a.created_at DESC,a.id DESC LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("created", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt?.ToUniversalTime() ?? DBNull.Value);
        query.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)beforeId ?? DBNull.Value);
        var rows = new List<AttachmentMetadata>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadAttachmentMetadata(reader)); return rows;
    }
    public async Task<AttachmentMetadata?> ChangeAttachmentLifecycleAsync(Guid organization, Guid card, Guid attachment,
        long version, AttachmentLifecycleState from, AttachmentLifecycleState to, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Lifecycle writes require the owning command transaction.");
        AttachmentLifecycleTransition.Require(from, to, actor);
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"""
            UPDATE attachments a SET lifecycle_state=@next,version=version+1,updated_at=@now,
              archived_at=CASE WHEN @next='ARCHIVED' THEN @now ELSE a.archived_at END,
              deleted_at=CASE WHEN @next='DELETED' THEN @now ELSE NULL END,
              deleted_by=CASE WHEN @next='DELETED' THEN @actor ELSE NULL END
            WHERE a.tenant_id=@tenant AND a.card_id=@card AND a.id=@id AND a.version=@version
              AND a.version<9223372036854775807 AND a.lifecycle_state=@previous AND a.updated_at<=@now
            RETURNING {AttachmentMetadataColumns};
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("id", attachment);
        query.Parameters.AddWithValue("version", version); query.Parameters.AddWithValue("previous", from.ToString().ToUpperInvariant());
        query.Parameters.AddWithValue("next", to.ToString().ToUpperInvariant()); query.Parameters.AddWithValue("actor", actor);
        query.Parameters.AddWithValue("now", AttachmentMetadataMapping.DatabaseTimestamp(now));
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAttachmentMetadata(reader) : null;
    }
}

internal static class AttachmentLifecycleTransition
{
    public static void Require(AttachmentLifecycleState from, AttachmentLifecycleState to, Guid actor)
    {
        if (actor == Guid.Empty || (from, to) is not ((AttachmentLifecycleState.Active, AttachmentLifecycleState.Archived)
            or (AttachmentLifecycleState.Archived, AttachmentLifecycleState.Active) or (AttachmentLifecycleState.Archived, AttachmentLifecycleState.Deleted)))
            throw new ArgumentException("Attachment lifecycle transition is invalid.");
    }
}
