using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkNotificationStore
{
    // Eligibility precedes ordering/limit; departed private Board notifications
    // never consume the bounded visible window or expose a pagination cursor.
    private const string VisibleNotifications = """
        SELECT n.id,n.tenant_id,n.board_id,n.card_id,n.event_id,n.recipient_id,n.actor_id,n.card_version,n.created_at,n.read_at,n.notification_type
        FROM card_assignment_notifications n
        JOIN organizations o ON o.id=n.tenant_id
        JOIN organization_members m ON m.tenant_id=n.tenant_id AND m.user_id=n.recipient_id
        JOIN users u ON u.id=n.recipient_id
        JOIN boards b ON b.tenant_id=n.tenant_id AND b.id=n.board_id
        JOIN cards c ON c.tenant_id=n.tenant_id AND c.board_id=n.board_id AND c.id=n.card_id
        JOIN board_lists l ON l.tenant_id=c.tenant_id AND l.board_id=c.board_id AND l.id=c.list_id
        WHERE n.tenant_id=@tenant AND n.recipient_id=@recipient AND o.status IN ('ACTIVE','ARCHIVED')
          AND m.status='ACTIVE' AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified)
          AND b.lifecycle_state='ACTIVE' AND c.lifecycle_state='ACTIVE' AND l.lifecycle_state='ACTIVE'
          AND (m.role IN ('OWNER','ADMIN') OR b.visibility IN ('PUBLIC','ORGANIZATION') OR EXISTS(
            SELECT 1 FROM board_members bm WHERE bm.tenant_id=n.tenant_id AND bm.board_id=n.board_id
              AND bm.user_id=n.recipient_id AND bm.status='ACTIVE'))
        """;

    public Task<IReadOnlyList<CardNotification>> ListVisibleAsync(Guid organizationId, Guid recipientId,
        NotificationCursor? after, bool requireVerifiedEmail, CancellationToken cancellationToken) =>
        ReadVisibleAsync(organizationId, recipientId, after, null, requireVerifiedEmail, cancellationToken);

    public Task<IReadOnlyList<CardNotification>> FindVisibleAsync(Guid organizationId, Guid recipientId,
        IReadOnlyCollection<Guid> ids, bool requireVerifiedEmail, CancellationToken cancellationToken) =>
        ids.Count == 0 ? Task.FromResult<IReadOnlyList<CardNotification>>([]) :
        ReadVisibleAsync(organizationId, recipientId, null, ids, requireVerifiedEmail, cancellationToken);

    private async Task<IReadOnlyList<CardNotification>> ReadVisibleAsync(Guid org, Guid recipient,
        NotificationCursor? after, IReadOnlyCollection<Guid>? ids, bool verified, CancellationToken ct)
    {
        if (ids is { Count: > 51 }) throw new ArgumentException("Notification windows contain at most 51 records.", nameof(ids));
        if (!connections.HasCommandScope(org)) throw new InvalidOperationException("Inbox reads require an owning command transaction.");
        await using var session = await connections.OpenTenantSessionAsync(org, ct);
        await using var query = new NpgsqlCommand(VisibleNotifications + (ids is null
            ? " AND (@after IS NULL OR (n.created_at,n.id)<(@created,@after)) ORDER BY n.created_at DESC,n.id DESC LIMIT 51;"
            : " AND n.id=ANY(@ids) ORDER BY n.created_at DESC,n.id DESC;"), session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", org); query.Parameters.AddWithValue("recipient", recipient);
        query.Parameters.AddWithValue("verified", verified);
        if (ids is null)
        {
            query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after?.Id ?? DBNull.Value);
            query.Parameters.AddWithValue("created", NpgsqlDbType.TimestampTz, (object?)after?.CreatedAt ?? DBNull.Value);
        }
        else query.Parameters.AddWithValue("ids", ids.ToArray());
        var result = new List<CardNotification>();
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetGuid(4), reader.GetGuid(5), reader.GetGuid(6), reader.GetInt64(7),
                reader.GetFieldValue<DateTimeOffset>(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9))
                { NotificationType = reader.GetString(10) });
        return result;
    }

    public async Task<IReadOnlyList<NotificationReadAcknowledgment>> MarkReadAsync(Guid org, Guid recipient,
        IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count is < 1 or > 50) throw new ArgumentException("Select between one and 50 notifications.", nameof(ids));
        if (!connections.HasCommandScope(org)) throw new InvalidOperationException("Notification read changes require their owning transaction.");
        await using var session = await connections.OpenTenantSessionAsync(org, ct);
        await using var query = new NpgsqlCommand("""
            UPDATE card_assignment_notifications SET read_at=COALESCE(read_at,GREATEST(created_at,@now))
            WHERE tenant_id=@tenant AND recipient_id=@recipient AND id=ANY(@ids)
            RETURNING id,read_at;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", org); query.Parameters.AddWithValue("recipient", recipient);
        query.Parameters.AddWithValue("ids", ids.ToArray()); query.Parameters.AddWithValue("now", now);
        var result = new List<NotificationReadAcknowledgment>();
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1)));
        return result.OrderBy(n => n.Id.ToString("N"), StringComparer.Ordinal).ToArray();
    }
}
