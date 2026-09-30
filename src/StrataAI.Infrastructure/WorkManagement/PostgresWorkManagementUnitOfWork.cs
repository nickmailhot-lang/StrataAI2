using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresWorkManagementUnitOfWork(
    PostgresConnectionFactory connections,
    ILogger<PostgresWorkManagementUnitOfWork> logger) : IWorkManagementUnitOfWork
{
    public async Task<WorkOperation<T>> ExecuteAsync<T>(Guid organizationId,
        Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteTenantCommandAsync(organizationId, operation,
                result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Work command lacked a database acknowledgment for Organization {OrganizationId}; database code {DatabaseCode}.",
                organizationId, exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return WorkOperation<T>.Failure("work_storage_unavailable");
        }
    }
}
