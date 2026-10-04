using Npgsql;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<bool> GetStarAsync(Guid boardId, Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult(_starred.Contains((boardId, userId)));
    }
}

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<bool> GetStarAsync(Guid boardId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveBoardTenantAsync(boardId, cancellationToken);
        if (tenant is null) return false;
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenant.Value, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT starred FROM user_board_preferences
            WHERE tenant_id=@tenant AND board_id=@board AND user_id=@actor;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenant.Value); command.Parameters.AddWithValue("board", boardId); command.Parameters.AddWithValue("actor", userId);
        return await command.ExecuteScalarAsync(cancellationToken) as bool? ?? false;
    }
}
