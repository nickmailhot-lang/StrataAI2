using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresActivityFeedStore(PostgresConnectionFactory connections) : IActivityFeedStore
{
    public async Task<IReadOnlyList<ActivityEventSource>> ReadAsync(ActivityCursorBinding binding, ActivityCursor? before,
        CancellationToken ct = default)
    {
        if (binding.OrganizationId == Guid.Empty || binding.ViewerId == Guid.Empty || binding.TargetId == Guid.Empty ||
            !Enum.IsDefined(binding.Kind) || !connections.HasCommandScope(binding.OrganizationId))
            throw new InvalidOperationException("Activity candidates require the owning Work transaction.");
        ActivityEventSourceWindow.RequireCursor(before?.CreatedAt, before?.EventId); ct.ThrowIfCancellationRequested();
        var sources = binding.Kind == ActivityTargetKind.Board ? """
            SELECT e.* FROM work_events e WHERE e.tenant_id=@tenant AND e.board_id=@target
            """ : """
            SELECT e.* FROM work_events e WHERE e.tenant_id=@tenant AND e.entity_type='Card' AND e.entity_id=@target
            UNION ALL
            SELECT e.* FROM work_events e JOIN watch_subscriptions w
              ON w.tenant_id=e.tenant_id AND w.id=e.watch_subscription_id
            WHERE e.tenant_id=@tenant AND w.user_id=@viewer AND w.entity_type='CARD' AND w.entity_id=@target
            UNION ALL
            SELECT e.* FROM work_events e JOIN card_reminders r ON r.tenant_id=e.tenant_id AND r.id=e.reminder_id
            WHERE e.tenant_id=@tenant AND r.user_id=@viewer AND r.card_id=@target
            """;
        var sql = "WITH sources AS (" + sources + """
            ), targets AS (
              SELECT e.*,
                CASE e.entity_type WHEN 'WatchSubscription' THEN
                  CASE w.entity_type WHEN 'BOARD' THEN 'Board' WHEN 'LIST' THEN 'List' WHEN 'CARD' THEN 'Card' END
                  WHEN 'Reminder' THEN 'Card' ELSE e.entity_type END AS target_type,
                CASE e.entity_type WHEN 'WatchSubscription' THEN w.entity_id WHEN 'Reminder' THEN r.card_id ELSE e.entity_id END AS target_id,
                CASE e.entity_type WHEN 'WatchSubscription' THEN w.user_id WHEN 'Reminder' THEN r.user_id END AS private_owner
              FROM sources e
              LEFT JOIN watch_subscriptions w ON w.tenant_id=e.tenant_id AND w.id=e.watch_subscription_id
              LEFT JOIN card_reminders r ON r.tenant_id=e.tenant_id AND r.id=e.reminder_id
            )
            SELECT t.event_id,t.tenant_id,t.board_id,t.actor_id,t.activity_actor_label,t.event_type,
              t.entity_type,t.entity_id,t.entity_version,t.created_at
            FROM targets t
            JOIN organization_members m ON m.tenant_id=t.tenant_id AND m.user_id=@viewer AND m.status='ACTIVE'
            JOIN organizations o ON o.id=m.tenant_id AND o.status IN ('ACTIVE','ARCHIVED')
            JOIN boards sb ON sb.tenant_id=t.tenant_id AND sb.id=t.board_id AND sb.lifecycle_state<>'DELETED'
            LEFT JOIN board_members sm ON sm.tenant_id=t.tenant_id AND sm.board_id=sb.id AND sm.user_id=@viewer
            LEFT JOIN cards c ON t.target_type='Card' AND c.tenant_id=t.tenant_id AND c.id=t.target_id
            LEFT JOIN board_lists cl ON cl.tenant_id=c.tenant_id AND cl.id=c.list_id AND cl.board_id=c.board_id
            LEFT JOIN board_lists dl ON t.target_type='List' AND dl.tenant_id=t.tenant_id AND dl.id=t.target_id
            LEFT JOIN board_labels l ON t.target_type='Label' AND l.tenant_id=t.tenant_id AND l.id=t.target_id
            JOIN boards cb ON cb.tenant_id=t.tenant_id AND cb.lifecycle_state<>'DELETED' AND cb.id=
              CASE t.target_type WHEN 'Board' THEN t.target_id WHEN 'Card' THEN c.board_id WHEN 'List' THEN dl.board_id WHEN 'Label' THEN l.board_id END
            LEFT JOIN board_members cm ON cm.tenant_id=t.tenant_id AND cm.board_id=cb.id AND cm.user_id=@viewer
            WHERE (t.entity_type NOT IN ('WatchSubscription','Reminder') OR t.private_owner=@viewer)
              AND (t.entity_type<>'Board' OR t.entity_id=t.board_id)
              AND (sb.visibility IN ('PUBLIC','ORGANIZATION') OR m.role IN ('OWNER','ADMIN') OR sm.status='ACTIVE')
              AND (cb.visibility IN ('PUBLIC','ORGANIZATION') OR m.role IN ('OWNER','ADMIN') OR cm.status='ACTIVE')
              AND CASE t.target_type
                WHEN 'Board' THEN t.target_id=t.board_id
                WHEN 'Card' THEN c.id IS NOT NULL AND cl.id IS NOT NULL AND
                  (c.lifecycle_state<>'DELETED' AND cl.lifecycle_state<>'DELETED' OR m.role IN ('OWNER','ADMIN') OR cm.status='ACTIVE' AND cm.role='ADMIN')
                WHEN 'List' THEN dl.id IS NOT NULL AND
                  (dl.lifecycle_state<>'DELETED' OR m.role IN ('OWNER','ADMIN') OR cm.status='ACTIVE' AND cm.role='ADMIN')
                WHEN 'Label' THEN l.id IS NOT NULL AND
                  (l.status<>'DELETED' OR m.role IN ('OWNER','ADMIN') OR cm.status='ACTIVE' AND cm.role='ADMIN')
                ELSE false END
              AND (@before IS NULL OR (t.created_at,t.event_id)<(@before,@event))
            ORDER BY t.created_at DESC,t.event_id DESC LIMIT 51;
            """;
        await using var session = await connections.OpenTenantSessionAsync(binding.OrganizationId, ct);
        await using var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", binding.OrganizationId); query.Parameters.AddWithValue("viewer", binding.ViewerId);
        query.Parameters.AddWithValue("target", binding.TargetId);
        query.Parameters.AddWithValue("before", NpgsqlDbType.TimestampTz, (object?)before?.CreatedAt ?? DBNull.Value);
        query.Parameters.AddWithValue("event", NpgsqlDbType.Uuid, (object?)before?.EventId ?? DBNull.Value);
        var rows = new List<ActivityEventSource>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetGuid(7), reader.GetInt64(8), reader.GetFieldValue<DateTimeOffset>(9)));
        return Array.AsReadOnly(rows.ToArray());
    }
}
