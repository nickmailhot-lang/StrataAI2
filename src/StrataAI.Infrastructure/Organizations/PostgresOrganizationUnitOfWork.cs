using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class PostgresOrganizationUnitOfWork(
    PostgresConnectionFactory connections,
    ICommandActorAuthorization actors,
    ILogger<PostgresOrganizationUnitOfWork> logger) : IOrganizationUnitOfWork
{
    public async Task<OrganizationOperation<T>> ExecuteAsync<T>(
        Guid organizationId, Guid actorUserId, Guid? targetUserId, bool creating,
        Func<Task<OrganizationOperation<T>>> operation,
        CancellationToken cancellationToken = default, bool allowDeletionRecovery = false)
    {
        try
        {
            return await connections.ExecuteTenantCommandAsync(organizationId, async () =>
            {
                if (creating)
                {
                    await using var creationSession = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
                    // Serialize absent-parent creation and matching receipt recovery.
                    // Existing-parent locks still precede actor/session admission.
                    await using var creationLock = new NpgsqlCommand("""
                        SELECT pg_advisory_xact_lock(hashtextextended('strataai:organization:create:' || @tenant::text,0));
                        SELECT id FROM organizations WHERE id=@tenant FOR UPDATE;
                        """, creationSession.Connection, creationSession.Transaction);
                    creationLock.Parameters.AddWithValue("tenant", organizationId);
                    await creationLock.ExecuteNonQueryAsync(cancellationToken);
                }
                if (!creating)
                {
                    await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
                    // Serialize ownership decisions before reading the owner count or actor role.
                    // Work commands acquire this parent before their membership/board locks too.
                    await using var parent = new NpgsqlCommand("""
                        SELECT id FROM organizations WHERE id=@tenant
                          AND (status='ACTIVE' OR (@recovery AND status='DELETING')) FOR UPDATE;
                        """, session.Connection, session.Transaction);
                    parent.Parameters.AddWithValue("tenant", organizationId);
                    parent.Parameters.AddWithValue("recovery", allowDeletionRecovery);
                    if (await parent.ExecuteScalarAsync(cancellationToken) is null)
                        return OrganizationOperation<T>.Failure("organization_not_found");
                    // Lock existing actor and target rows in a stable order. Re-read their roles
                    // inside the service after any wait, including externally applied revocation.
                    await using var members = new NpgsqlCommand("""
                        SELECT user_id FROM organization_members
                        WHERE tenant_id=@tenant AND (user_id=@actor OR user_id=@target)
                        ORDER BY user_id FOR UPDATE;
                        """, session.Connection, session.Transaction);
                    members.Parameters.AddWithValue("tenant", organizationId);
                    members.Parameters.AddWithValue("actor", actorUserId);
                    members.Parameters.AddWithValue("target", targetUserId ?? actorUserId);
                    await using var reader = await members.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken)) { }
                }
                if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                    return OrganizationOperation<T>.Failure("session_unavailable");
                var result = await operation();
                // Wall-clock session expiry can occur during a database write
                // wait even while account/session locks prevent revocation.
                if (result.Succeeded && !await actors.VerifyAsync(actorUserId, cancellationToken))
                    return OrganizationOperation<T>.Failure("session_unavailable");
                return result;
            }, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Organization command lacked a database acknowledgment for {OrganizationId}; code {DatabaseCode}.",
                organizationId, exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return OrganizationOperation<T>.Failure("organization_storage_unavailable");
        }
    }
}
