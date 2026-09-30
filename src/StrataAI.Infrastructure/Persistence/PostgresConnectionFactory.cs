using Npgsql;
using StrataAI.Application.Runtime;

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

    public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT public.runtime_database_role_is_safe();", connection);
            try
            {
                if (await command.ExecuteScalarAsync(cancellationToken) is not true) throw new RuntimeDatabaseRoleException();
            }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedFunction or PostgresErrorCodes.InsufficientPrivilege)
            {
                throw new RuntimeDatabaseRoleException();
            }
            await using var schema = new NpgsqlCommand("""
                SELECT count(*) = 9 FROM public.schema_migrations WHERE version = ANY(ARRAY[
                  '001_foundation','002_audit_runtime','003_identity','004_organization_access_routing',
                  '005_invitation_routing','006_work_management','007_background_jobs',
                  '008_identity_delivery','009_runtime_role_guard']);
                """, connection);
            try
            {
                if (await schema.ExecuteScalarAsync(cancellationToken) is not true) throw new RuntimeDatabaseSchemaException();
            }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege)
            {
                throw new RuntimeDatabaseSchemaException();
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

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

        var connection = await OpenConnectionAsync(cancellationToken);
        NpgsqlTransaction? transaction = null;

        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken);
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
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
