using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkNotificationStore
{
    public Task AppendCardMentionsAsync(WorkEvent change, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken = default)
        => AppendBatchAsync(change, recipients, false, cancellationToken);

    public Task AppendCardActivitiesAsync(WorkEvent change, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken = default)
        => AppendBatchAsync(change, recipients, true, cancellationToken);

    private async Task AppendBatchAsync(WorkEvent change, IReadOnlyList<Guid> recipients, bool activity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients); cancellationToken.ThrowIfCancellationRequested();
        if (!connections.HasCommandScope(change.OrganizationId)) throw new InvalidOperationException("Batch notifications require the originating command transaction.");
        _ = activity ? CardNotification.FromActivity(change, change.ActorId) : CardNotification.FromMention(change, change.ActorId);
        if (recipients.Any(id => id == Guid.Empty) || recipients.Distinct().Count() != recipients.Count)
            throw new ArgumentException("Batch notification recipients must be distinct accounts.");
        var targets = recipients.Where(id => id != change.ActorId).ToArray();
        if (targets.Length == 0) return;
        await using var session = await connections.OpenTenantSessionAsync(change.OrganizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", change.OrganizationId); query.Parameters.AddWithValue("board", change.BoardId);
            query.Parameters.AddWithValue("card", change.EntityId); query.Parameters.AddWithValue("event", change.EventId);
            query.Parameters.AddWithValue("actor", change.ActorId); query.Parameters.AddWithValue("version", change.Version);
            query.Parameters.AddWithValue("created", change.CreatedAt); query.Parameters.AddWithValue("recipients", targets);
            query.Parameters.AddWithValue("type", change.EventType);
            return query;
        }
        await using (var insert = Query("""
            INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
            SELECT @tenant,gen_random_uuid(),@board,@card,@event,r.id,@actor,@version,@created,@type
            FROM unnest(@recipients) r(id) WHERE EXISTS(SELECT 1 FROM work_events e
              WHERE e.tenant_id=@tenant AND e.board_id=@board AND e.event_id=@event AND e.entity_type='Card'
                AND e.entity_id=@card AND e.event_type=@type AND e.actor_id=@actor
                AND e.entity_version=@version AND e.created_at=@created)
            ORDER BY r.id
            ON CONFLICT(tenant_id,event_id,recipient_id) DO NOTHING;
            """)) await insert.ExecuteNonQueryAsync(cancellationToken);
        // A fresh statement observes an exact concurrent duplicate after its
        // ON CONFLICT wait; new rows retain the first generated identity/readAt.
        await using var verify = Query("""
            SELECT count(*)=cardinality(@recipients) FROM card_assignment_notifications n
            JOIN work_events e ON e.tenant_id=n.tenant_id AND e.board_id=n.board_id AND e.event_id=n.event_id
            WHERE n.tenant_id=@tenant AND n.board_id=@board AND n.card_id=@card AND n.event_id=@event
              AND n.recipient_id=ANY(@recipients) AND n.actor_id=@actor AND n.card_version=@version AND n.created_at=@created
              AND n.notification_type=@type AND e.entity_type='Card' AND e.entity_id=@card
              AND e.event_type=@type AND e.actor_id=@actor AND e.entity_version=@version AND e.created_at=@created;
            """);
        if (await verify.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Assignment notification event was unavailable or reused.");
    }
}
