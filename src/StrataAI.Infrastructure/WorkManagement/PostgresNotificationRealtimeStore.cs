using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkNotificationStore
{
    public async Task<IReadOnlyList<NotificationRealtimeEvent>> ListRecipientEventsAsync(Guid organizationId,
        Guid recipientId, long after = 0, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || recipientId == Guid.Empty || after < 0)
            throw new ArgumentException("Invalid notification journal scope or cursor.");
        if (!connections.HasCommandScope(organizationId))
            throw new InvalidOperationException("Notification journal reads require their owning transaction.");
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT e.event_id,e.sequence,e.event_type,e.created_at,e.version,e.board_id,e.actor_id,
             n.id,n.tenant_id,n.board_id,n.card_id,n.event_id,n.recipient_id,n.actor_id,n.card_version,n.created_at,n.read_at
            FROM notification_events e JOIN card_assignment_notifications n
             ON n.tenant_id=e.tenant_id AND n.id=e.notification_id AND n.recipient_id=e.recipient_id
            WHERE e.tenant_id=@tenant AND e.recipient_id=@recipient AND e.sequence>@after
            ORDER BY e.sequence LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("recipient", recipientId);
        query.Parameters.AddWithValue("after", after);
        var events = new List<NotificationRealtimeEvent>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var notification = new CardNotification(reader.GetGuid(7), reader.GetGuid(8), reader.GetGuid(9), reader.GetGuid(10),
                reader.GetGuid(11), reader.GetGuid(12), reader.GetGuid(13), reader.GetInt64(14),
                reader.GetFieldValue<DateTimeOffset>(15), reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16));
            var time = reader.GetFieldValue<DateTimeOffset>(3);
            var change = reader.GetString(2) switch {
                "NOTIFICATION_CREATED" => NotificationRealtimeEvent.Created(notification, reader.GetGuid(0), reader.GetInt64(1)),
                "NOTIFICATION_READ" => NotificationRealtimeEvent.Read(notification, reader.GetGuid(0), reader.GetInt64(1), time),
                _ => throw new InvalidOperationException("Invalid persisted notification event.")
            };
            if (change.CreatedAt != time || change.Version != reader.GetInt64(4) || change.BoardId != reader.GetGuid(5) || change.ActorId != reader.GetGuid(6))
                throw new InvalidOperationException("Persisted notification event scope is inconsistent.");
            events.Add(change);
        }
        return events;
    }
}
