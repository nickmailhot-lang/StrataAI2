using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationDeletionObservationReader(PostgresConnectionFactory connections,
    ICommandActorAuthorization actors, ILogger<PostgresOrganizationDeletionObservationReader> logger)
    : IOrganizationDeletionObservationReader
{
    public async Task<OrganizationOperation<OrganizationDeletionObservation>> ReadAsync(Guid organizationId,
        Guid actorId, Guid requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(organizationId==Guid.Empty || actorId==Guid.Empty || requestId==Guid.Empty) return Missing();
        try
        {
            return await connections.ExecuteTenantCommandAsync(organizationId,async()=>{
                await using var scope=await connections.OpenTenantSessionAsync(organizationId,cancellationToken);
                // Parent precedes membership, account and session locks, just as
                // normal commands do. Deleted parents are admitted only here.
                await using(var parent=new NpgsqlCommand("SELECT id FROM organizations WHERE id=@tenant AND status IN ('DELETING','DELETED') FOR SHARE;",scope.Connection,scope.Transaction))
                {
                    parent.Parameters.AddWithValue("tenant",organizationId);
                    if(await parent.ExecuteScalarAsync(cancellationToken) is null)return Missing();
                }
                await using(var owner=new NpgsqlCommand("SELECT user_id FROM organization_members WHERE tenant_id=@tenant AND user_id=@actor AND status='ACTIVE' AND role='OWNER' FOR SHARE;",scope.Connection,scope.Transaction))
                {
                    owner.Parameters.AddWithValue("tenant",organizationId);owner.Parameters.AddWithValue("actor",actorId);
                    if(await owner.ExecuteScalarAsync(cancellationToken) is null)return Missing();
                }
                if(!await actors.VerifyAsync(actorId,cancellationToken))return SessionUnavailable();
                OrganizationDeletionObservation? observed=null;
                await using(var query=new NpgsqlCommand("""
                    SELECT r.request_id,o.version,
                     CASE WHEN o.status='DELETED' THEN 'COMPLETED' ELSE 'PENDING' END,
                     CASE WHEN o.status='DELETED' THEN e.event_id END,
                     CASE WHEN o.status='DELETED' THEN o.deleted_at END
                    FROM organization_deletion_requests r JOIN organizations o ON o.id=r.tenant_id
                     JOIN organization_deletion_progress p USING(tenant_id,request_id)
                     LEFT JOIN organization_lifecycle_events e ON e.tenant_id=r.tenant_id
                    WHERE r.tenant_id=@tenant AND r.request_id=@request AND r.actor_id=@actor
                     AND ((o.status='DELETING' AND o.version=r.accepted_version AND p.phase<>'COMPLETE')
                      OR (o.status='DELETED' AND o.version=r.accepted_version+1 AND o.deleted_by=r.actor_id
                       AND p.phase='COMPLETE' AND p.completed_at=o.deleted_at AND e.event_type='ORGANIZATION_DELETED'
                       AND e.actor_id=r.actor_id AND e.entity_id=r.tenant_id AND e.entity_version=o.version
                       AND e.created_at=o.deleted_at AND e.correlation_id=r.correlation_id));
                    """,scope.Connection,scope.Transaction))
                {
                    query.Parameters.AddWithValue("tenant",organizationId);query.Parameters.AddWithValue("actor",actorId);query.Parameters.AddWithValue("request",requestId);
                    await using var row=await query.ExecuteReaderAsync(cancellationToken);
                    if(await row.ReadAsync(cancellationToken))observed=new(row.GetGuid(0),row.GetString(2),row.GetInt64(1),
                        row.IsDBNull(3)?null:row.GetGuid(3),row.IsDBNull(4)?null:row.GetFieldValue<DateTimeOffset>(4));
                }
                if(observed is null)return Missing();
                if(!await actors.VerifyAsync(actorId,cancellationToken))return SessionUnavailable();
                return OrganizationOperation<OrganizationDeletionObservation>.Success(observed);
            },result=>result.Succeeded,cancellationToken);
        }
        catch(NpgsqlException e)
        {
            logger.LogWarning("Deletion observation unavailable for {OrganizationId}; database code {DatabaseCode}.",organizationId,
                e is PostgresException pg?pg.SqlState:"connection_error");
            return OrganizationOperation<OrganizationDeletionObservation>.Failure("organization_storage_unavailable");
        }
    }
    private static OrganizationOperation<OrganizationDeletionObservation> Missing()=>OrganizationOperation<OrganizationDeletionObservation>.Failure("organization_not_found");
    private static OrganizationOperation<OrganizationDeletionObservation> SessionUnavailable()=>OrganizationOperation<OrganizationDeletionObservation>.Failure("session_unavailable");
}
