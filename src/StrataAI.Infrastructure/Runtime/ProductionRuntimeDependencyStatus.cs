using Npgsql;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Runtime;

internal sealed class ProductionRuntimeDependencyStatus(
    PostgresConnectionFactory connectionFactory) : IRuntimeDependencyStatus
{
    public RuntimeMode Mode => RuntimeMode.Production;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection =
                await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) == 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or RuntimeDatabaseRoleException)
        {
            return false;
        }
    }
}
