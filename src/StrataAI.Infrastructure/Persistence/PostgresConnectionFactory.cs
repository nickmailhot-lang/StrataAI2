using Npgsql;

namespace StrataAI.Infrastructure.Persistence;

/// <summary>
/// Owns the production PostgreSQL data source. Tenant-owned operations should use
/// OpenTenantSessionAsync so PostgreSQL RLS receives the Organization context.
/// </summary>
public sealed class PostgresConnectionFactory : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A PostgreSQL connection string is required.",
                nameof(connectionString));
        }

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default) =>
        _dataSource.OpenConnectionAsync(cancellationToken);

    public async Task<TenantDbSession> OpenTenantSessionAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization ID cannot be empty.",
                nameof(organizationId));
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tenant_id, true);",
                connection,
                transaction);
            command.Parameters.AddWithValue(
                "tenant_id",
                organizationId.ToString());
            await command.ExecuteScalarAsync(cancellationToken);

            return new TenantDbSession(connection, transaction, organizationId);
        }
        catch
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
