using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

// Registration follows the lifecycle state/source migration and restricted
// PostgreSQL contract. No normal command or deletion exception is broadened.
internal sealed class PostgresOrganizationOperationalLifecycleUnitOfWork(PostgresConnectionFactory connections,
    ICommandActorAuthorization actors, OrganizationOperationalLifecycleCommandScope lifecycle,
    ILogger<PostgresOrganizationOperationalLifecycleUnitOfWork> logger) : IOrganizationOperationalLifecycleUnitOfWork
{
    public async Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization, Guid actor,
        Func<Task<OrganizationOperation<T>>> action, CancellationToken ct)
    {
        if (organization == Guid.Empty || actor == Guid.Empty) return OrganizationOperation<T>.Failure("organization_not_found");
        try
        {
            return await connections.ExecuteTenantCommandAsync(organization, async () =>
            {
                await using var session = await connections.OpenTenantSessionAsync(organization, ct);
                // Match operational command lock order. This retained parent lock
                // serializes the state transition with pending operational writes.
                await using (var parent = new NpgsqlCommand("""
                    SELECT id FROM organizations WHERE id=@tenant
                     AND status IN ('ACTIVE','SUSPENDED','DEACTIVATED') FOR UPDATE;
                    """, session.Connection, session.Transaction))
                {
                    parent.Parameters.AddWithValue("tenant", organization);
                    if (await parent.ExecuteScalarAsync(ct) is null) return OrganizationOperation<T>.Failure("organization_not_found");
                }
                await using (var membership = new NpgsqlCommand("""
                    SELECT id FROM organization_members WHERE tenant_id=@tenant AND user_id=@actor FOR UPDATE;
                    """, session.Connection, session.Transaction))
                {
                    membership.Parameters.AddWithValue("tenant", organization); membership.Parameters.AddWithValue("actor", actor);
                    await using var rows = await membership.ExecuteReaderAsync(ct);
                    while (await rows.ReadAsync(ct)) { }
                }
                if (!await actors.VerifyAsync(actor, ct)) return OrganizationOperation<T>.Failure("session_unavailable");
                using var capability = lifecycle.Enter(organization, actor);
                var result = await action();
                if (result.Succeeded && !await actors.VerifyAsync(actor, ct))
                    return OrganizationOperation<T>.Failure("session_unavailable");
                ct.ThrowIfCancellationRequested(); return result;
            }, result => result.Succeeded, ct);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Organization lifecycle command lacked a database acknowledgment for {OrganizationId}; code {DatabaseCode}.",
                organization, exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return OrganizationOperation<T>.Failure("organization_storage_unavailable");
        }
    }
}
