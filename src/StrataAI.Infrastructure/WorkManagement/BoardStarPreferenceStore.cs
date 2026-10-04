using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed record StoredBoardStarPreference(bool Starred, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version, Guid Id);

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<BoardStarState> GetStarAsync(Guid boardId, Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult(_starred.TryGetValue((boardId, userId), out var preference)
            ? new BoardStarState(preference.Starred, preference.CreatedAt, preference.UpdatedAt, preference.Version)
            : new BoardStarState(false, null, null, 0));
    }
}

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<BoardStarState> GetStarAsync(Guid boardId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveBoardTenantAsync(boardId, cancellationToken);
        if (tenant is null) return new(false, null, null, 0);
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenant.Value, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT starred,created_at,updated_at,version FROM user_board_preferences
            WHERE tenant_id=@tenant AND board_id=@board AND user_id=@actor;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenant.Value); command.Parameters.AddWithValue("board", boardId); command.Parameters.AddWithValue("actor", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetInt64(3))
            : new(false, null, null, 0);
    }
}
