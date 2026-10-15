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
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var connection =
                await connectionFactory.OpenReadinessConnectionAsync(probe.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(probe.Token);
            return Convert.ToInt32(result) == 1;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && probe.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is NpgsqlException or RuntimeDatabaseRoleException or RuntimeDatabaseSchemaException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }
}
