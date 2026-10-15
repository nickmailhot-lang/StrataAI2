using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.BackgroundJobs;

// An independent, single-connection diagnostic pool cannot exhaust the normal
// command/readiness pools. Reuse their restricted-role/schema/transport guards.
public sealed class PostgresOrganizationJobBacklogReader(string connectionString)
    : IOrganizationJobBacklogReader, IAsyncDisposable
{
    private readonly PostgresConnectionFactory connections = new(new NpgsqlConnectionStringBuilder(connectionString)
    { Timeout = 2, CommandTimeout = 2, MinPoolSize = 0, MaxPoolSize = 1 }.ConnectionString);

    public async Task<OrganizationJobBacklog> ReadAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization scope is required.", nameof(organizationId));
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var scope = new NpgsqlCommand("SET TRANSACTION READ ONLY; SELECT set_config('app.tenant_id',@tenant,true);", connection, transaction))
        {
            scope.Parameters.AddWithValue("tenant", organizationId.ToString());
            await scope.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FILTER (WHERE state='PENDING'), count(*) FILTER (WHERE state='RUNNING')
            FROM background_jobs WHERE tenant_id=@tenant AND state IN ('PENDING','RUNNING');
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", organizationId);
        OrganizationJobBacklog result;
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
