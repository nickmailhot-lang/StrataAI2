using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresActivityEventSourceStore(PostgresConnectionFactory connections) : IActivityEventSourceStore
{
    public async Task<IReadOnlyList<ActivityEventSource>> ReadBoardWindowAsync(Guid organizationId, Guid boardId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeEventId, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || boardId == Guid.Empty || !connections.HasCommandScope(organizationId))
            throw new InvalidOperationException("Activity sources require the owning Work transaction.");
        ActivityEventSourceWindow.RequireCursor(beforeCreatedAt, beforeEventId); ct.ThrowIfCancellationRequested();
        await using var session = await connections.OpenTenantSessionAsync(organizationId, ct);
        await using var query = new NpgsqlCommand("""
            SELECT event_id,actor_id,activity_actor_label,event_type,entity_type,entity_id,entity_version,created_at
            FROM work_events WHERE tenant_id=@tenant AND board_id=@board
              AND (@before IS NULL OR (created_at,event_id)<(@before,@id))
            ORDER BY created_at DESC,event_id DESC LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("board", boardId);
        query.Parameters.AddWithValue("before", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt ?? DBNull.Value);
        query.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)beforeEventId ?? DBNull.Value);
        var rows = new List<ActivityEventSource>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), organizationId, boardId, reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetGuid(5), reader.GetInt64(6), reader.GetFieldValue<DateTimeOffset>(7)));
        return Array.AsReadOnly(rows.ToArray());
    }
}
