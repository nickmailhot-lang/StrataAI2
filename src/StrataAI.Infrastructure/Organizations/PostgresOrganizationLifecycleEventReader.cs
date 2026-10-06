using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationLifecycleEventReader(PostgresConnectionFactory connections,
    ICommandActorAuthorization actors, ILogger<PostgresOrganizationLifecycleEventReader> logger) : IOrganizationLifecycleEventReader
{
    public async Task<OrganizationOperation<OrganizationLifecyclePage>> ReadAsync(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (organizationId == Guid.Empty || actorId == Guid.Empty) return Missing();
        try
        {
            return await connections.ExecuteTenantCommandAsync(organizationId, async () =>
            {
                await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
                string? status;
                // Match command lock order. Only this content-free channel can
                // retain Internal membership admission after graph withdrawal.
                await using (var parent = new NpgsqlCommand("SELECT status FROM organizations WHERE id=@tenant AND status IN ('ACTIVE','DELETING','DELETED') FOR SHARE;", session.Connection, session.Transaction))
                {
                    parent.Parameters.AddWithValue("tenant", organizationId);
                    status = await parent.ExecuteScalarAsync(cancellationToken) as string;
                }
                if (status is null) return Missing();
                await using (var membership = new NpgsqlCommand("SELECT id FROM organization_members WHERE tenant_id=@tenant AND user_id=@actor AND status='ACTIVE' FOR SHARE;", session.Connection, session.Transaction))
                {
                    membership.Parameters.AddWithValue("tenant", organizationId); membership.Parameters.AddWithValue("actor", actorId);
                    if (await membership.ExecuteScalarAsync(cancellationToken) is null) return Missing();
                }
                await using (var account = new NpgsqlCommand("SELECT id FROM users WHERE id=@actor AND status='ACTIVE' FOR SHARE;", session.Connection, session.Transaction))
                {
                    account.Parameters.AddWithValue("actor", actorId);
                    if (await account.ExecuteScalarAsync(cancellationToken) is null) return SessionUnavailable();
                }
                if (!await actors.VerifyAsync(actorId, cancellationToken)) return SessionUnavailable();
                OrganizationLifecyclePage? page = status == "ACTIVE" ? new("ACTIVE", []) : null;
                if (page is null)
                {
                    await using var query = new NpgsqlCommand("""
                        SELECT e.event_id,e.actor_id,e.entity_version,e.created_at,e.ready_at IS NOT NULL
                        FROM organizations o JOIN organization_deletion_requests r ON r.tenant_id=o.id
                         JOIN organization_deletion_progress p USING(tenant_id,request_id)
                         LEFT JOIN organization_lifecycle_events e ON e.tenant_id=o.id
                        WHERE o.id=@tenant AND
                         ((o.status='DELETING' AND o.version=r.accepted_version AND p.phase<>'COMPLETE')
                          OR (o.status='DELETED' AND o.version=r.accepted_version+1 AND o.deleted_by=r.actor_id
                           AND p.phase='COMPLETE' AND p.completed_at=o.deleted_at AND e.event_type='ORGANIZATION_DELETED'
                           AND e.actor_id=r.actor_id AND e.entity_type='Organization' AND e.entity_id=o.id
                           AND e.entity_version=o.version AND e.created_at=o.deleted_at AND e.correlation_id=r.correlation_id
                           AND e.metadata='{}'::jsonb));
                        """, session.Connection, session.Transaction);
                    query.Parameters.AddWithValue("tenant", organizationId);
                    await using var row = await query.ExecuteReaderAsync(cancellationToken);
                    if (await row.ReadAsync(cancellationToken))
                    {
                        page = status == "DELETED" && row.GetBoolean(4)
                            ? new("COMPLETED", [new(row.GetGuid(0), "ORGANIZATION_DELETED", row.GetGuid(1), organizationId,
                                row.GetInt64(2), row.GetFieldValue<DateTimeOffset>(3))])
                            : new("PENDING", []);
                    }
                }
                if (page is null) return Missing();
                if (!await actors.VerifyAsync(actorId, cancellationToken)) return SessionUnavailable();
                return OrganizationOperation<OrganizationLifecyclePage>.Success(page);
            }, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException error)
        {
            logger.LogWarning("Organization lifecycle replay unavailable for {OrganizationId}; database code {DatabaseCode}.",
                organizationId, error is PostgresException pg ? pg.SqlState : "connection_error");
            return OrganizationOperation<OrganizationLifecyclePage>.Failure("organization_storage_unavailable");
        }
    }
    private static OrganizationOperation<OrganizationLifecyclePage> Missing() => OrganizationOperation<OrganizationLifecyclePage>.Failure("organization_not_found");
    private static OrganizationOperation<OrganizationLifecyclePage> SessionUnavailable() => OrganizationOperation<OrganizationLifecyclePage>.Failure("session_unavailable");
}
