using Npgsql;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

// The existing global mail capability has no Organization/password access.
// Sampling has a dedicated bounded pool, separate from delivery/readiness.
public sealed class PostgresIdentityDeliveryBacklogReader(string connectionString)
    : IIdentityDeliveryBacklogReader, IAsyncDisposable
{
    private readonly PostgresConnectionFactory connections = new(new NpgsqlConnectionStringBuilder(connectionString)
    { Timeout = 2, CommandTimeout = 2, MinPoolSize = 0, MaxPoolSize = 1 }.ConnectionString);

    public async Task<IdentityDeliveryBacklog> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var scope = new NpgsqlCommand("SET TRANSACTION READ ONLY; SELECT set_config('app.service_scope','GLOBAL_IDENTITY_MAIL',true);", connection, transaction))
            await scope.ExecuteNonQueryAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FILTER (WHERE state='PENDING'), count(*) FILTER (WHERE state='RUNNING')
            FROM identity_delivery_jobs WHERE state IN ('PENDING','RUNNING');
            """, connection, transaction);
        IdentityDeliveryBacklog result;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            result = new(reader.GetInt64(0), reader.GetInt64(1));
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public ValueTask DisposeAsync() => connections.DisposeAsync();
}
