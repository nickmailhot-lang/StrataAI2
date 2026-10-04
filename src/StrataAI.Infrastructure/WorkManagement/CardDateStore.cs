using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<CardRecord?> SetDatesAsync(Guid organizationId, Guid boardId, Guid cardId, CardDateValues values,
        long version, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card) || card.OrganizationId != organizationId || card.BoardId != boardId ||
                card.Version != version || card.LifecycleState != WorkItemLifecycleState.Active) return Task.FromResult<CardRecord?>(null);
            var updated = card with { StartAt = values.StartAt, DueAt = values.DueAt, DueTimezone = values.DueTimezone,
                DueHasTime = values.DueHasTime, DueComplete = values.DueComplete, Version = card.Version + 1,
                UpdatedAt = now > card.UpdatedAt ? now : card.UpdatedAt };
            _cards[cardId] = updated;
            return Task.FromResult<CardRecord?>(updated);
        }
    }
}

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<CardRecord?> SetDatesAsync(Guid organizationId, Guid boardId, Guid cardId, CardDateValues values,
        long version, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!connectionFactory.HasCommandScope(organizationId)) throw new InvalidOperationException("Card dates require the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE cards SET start_at=@start,due_at=@due,due_timezone=@zone,due_has_time=@timed,due_complete=@complete,
                version=version+1,updated_at=GREATEST(updated_at,@now)
            WHERE tenant_id=@tenant AND board_id=@board AND id=@card AND version=@version AND lifecycle_state='ACTIVE'
            RETURNING id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,created_at,updated_at,version,
                start_at,due_at,due_timezone,due_has_time,due_complete,archived_at,deleted_at;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("card", cardId); command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("start", NpgsqlDbType.TimestampTz, (object?)values.StartAt ?? DBNull.Value);
        command.Parameters.AddWithValue("due", NpgsqlDbType.TimestampTz, (object?)values.DueAt ?? DBNull.Value);
        command.Parameters.AddWithValue("zone", NpgsqlDbType.Text, (object?)values.DueTimezone ?? DBNull.Value);
        command.Parameters.AddWithValue("timed", values.DueHasTime); command.Parameters.AddWithValue("complete", values.DueComplete);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCard(reader) : null;
    }
}
