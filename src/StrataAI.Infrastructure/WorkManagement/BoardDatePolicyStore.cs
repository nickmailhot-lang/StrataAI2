using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IBoardDatePolicyStore
{
    public Task<BoardRecord?> SetDatePolicyAsync(Guid organizationId, Guid boardId, string? timezone,
        long version, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board) || board.OrganizationId != organizationId ||
                board.Version != version || board.LifecycleState != BoardLifecycleState.Active) return Task.FromResult<BoardRecord?>(null);
            var updated = board with { DateTimezoneOverride = timezone, Version = version + 1,
                UpdatedAt = now > board.UpdatedAt ? now : board.UpdatedAt };
            _boards[boardId] = updated;
            return Task.FromResult<BoardRecord?>(updated);
        }
    }
}

internal sealed partial class PostgresWorkManagementStore : IBoardDatePolicyStore
{
    public async Task<BoardRecord?> SetDatePolicyAsync(Guid organizationId, Guid boardId, string? timezone,
        long version, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organizationId)) throw new InvalidOperationException("Board date policy requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, ct);
        await using var command = new NpgsqlCommand($"""
            UPDATE boards SET date_timezone_override=@zone,version=version+1,updated_at=GREATEST(updated_at,@now)
            WHERE tenant_id=@tenant AND id=@board AND version=@version AND lifecycle_state='ACTIVE'
            RETURNING {BoardColumns};
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("zone", NpgsqlDbType.Text, (object?)timezone ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBoard(reader) : null;
    }
}
