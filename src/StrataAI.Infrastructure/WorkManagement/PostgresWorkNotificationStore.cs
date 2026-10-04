using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkNotificationStore(PostgresConnectionFactory connections) : IWorkNotificationStore, INotificationInboxStore, INotificationRealtimeStore
{
    public async Task AppendCardAssignmentAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => await AppendAsync(change, CardNotification.From(change, recipientId), cancellationToken);

    public async Task AppendCardActivityAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => await AppendAsync(change, CardNotification.FromActivity(change, recipientId), cancellationToken);

    public async Task AppendCardMentionAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => await AppendAsync(change, CardNotification.FromMention(change, recipientId), cancellationToken);

    private async Task AppendAsync(WorkEvent change, CardNotification? item, CancellationToken cancellationToken)
    {
        if (item is null) return;
        if (!connections.HasCommandScope(item.OrganizationId))
            throw new InvalidOperationException("Assignment notifications require the originating command transaction.");
        await using var session = await connections.OpenTenantSessionAsync(item.OrganizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            WITH expected AS (
              SELECT e.event_id FROM work_events e WHERE e.tenant_id=@tenant AND e.board_id=@board AND e.event_id=@event
              AND e.entity_type='Card' AND e.entity_id=@card AND e.event_type=@eventType
              AND e.actor_id=@actor AND e.entity_version=@version AND e.created_at=@created
            ), inserted AS (
              INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
              SELECT @tenant,@id,@board,@card,@event,@recipient,@actor,@version,@created,@type FROM expected
              ON CONFLICT(tenant_id,event_id,recipient_id) DO NOTHING RETURNING id
            ) SELECT EXISTS(SELECT 1 FROM inserted) OR EXISTS(
              SELECT 1 FROM card_assignment_notifications n JOIN expected e ON e.event_id=n.event_id
              WHERE n.tenant_id=@tenant AND n.event_id=@event AND n.recipient_id=@recipient
                AND n.board_id=@board AND n.card_id=@card AND n.actor_id=@actor
                AND n.card_version=@version AND n.created_at=@created AND n.notification_type=@type);
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", item.OrganizationId); query.Parameters.AddWithValue("id", item.Id);
        query.Parameters.AddWithValue("board", item.BoardId); query.Parameters.AddWithValue("card", item.CardId);
        query.Parameters.AddWithValue("event", item.EventId); query.Parameters.AddWithValue("recipient", item.RecipientId);
        query.Parameters.AddWithValue("actor", item.ActorId); query.Parameters.AddWithValue("version", item.CardVersion);
        query.Parameters.AddWithValue("created", item.CreatedAt);
        query.Parameters.AddWithValue("eventType", change.EventType); query.Parameters.AddWithValue("type", item.NotificationType);
        if (await query.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Assignment notification event was unavailable or reused.");
    }

    public async Task<IReadOnlyList<CardNotification>> ListCardNotificationsAsync(Guid organizationId,
        Guid recipientId, Guid? after = null, CancellationToken cancellationToken = default)
    {
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT id,tenant_id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,read_at,notification_type
            FROM card_assignment_notifications WHERE tenant_id=@tenant AND recipient_id=@recipient
              AND (@after IS NULL OR id>@after) ORDER BY id LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("recipient", recipientId);
        query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<CardNotification>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetGuid(4), reader.GetGuid(5), reader.GetGuid(6), reader.GetInt64(7),
                reader.GetFieldValue<DateTimeOffset>(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9))
                { NotificationType = reader.GetString(10) });
        return result;
    }
}
