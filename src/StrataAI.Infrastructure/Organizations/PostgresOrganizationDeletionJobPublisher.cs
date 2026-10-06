using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationDeletionJobPublisher(PostgresConnectionFactory connections,
    PostgresBackgroundJobStore jobs) : IOrganizationDeletionJobPublisher
{
    public async Task<bool> PublishAsync(Guid organizationId, Guid actorId, Guid requestId,
        long acceptedVersion, string correlationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!connections.HasCommandScope(organizationId))
            throw new InvalidOperationException("Deletion publication requires its owning Organization transaction.");
        var attempt = new OrganizationDeletionAttempt(requestId, requestId, acceptedVersion);
        var job = OrganizationDeletionJobs.Create(organizationId, actorId, attempt, correlationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
            command.Parameters.AddWithValue("request", requestId); command.Parameters.AddWithValue("version", acceptedVersion);
            return command;
        }
        await using (var parent = Query("SELECT id FROM organizations WHERE id=@tenant AND status='DELETING' AND version=@version FOR UPDATE;"))
            if (await parent.ExecuteScalarAsync(cancellationToken) is null) throw Unavailable();
        await using (var owner = Query("""
            SELECT m.user_id FROM organization_members m JOIN users u ON u.id=m.user_id
            WHERE m.tenant_id=@tenant AND m.user_id=@actor AND m.status='ACTIVE' AND m.role='OWNER' AND u.status='ACTIVE'
            FOR SHARE OF m,u;
            """))
            if (await owner.ExecuteScalarAsync(cancellationToken) is null) throw Unavailable();
        var existing = false;
        await using (var read = Query("SELECT request_id,actor_id,accepted_version,correlation_id FROM organization_deletion_requests WHERE tenant_id=@tenant;"))
        await using (var rows = await read.ExecuteReaderAsync(cancellationToken))
        {
            if (await rows.ReadAsync(cancellationToken))
            {
                if (rows.GetGuid(0) != requestId || rows.GetGuid(1) != actorId || rows.GetInt64(2) != acceptedVersion) throw Unavailable();
                existing = true;
                job = OrganizationDeletionJobs.Create(organizationId, actorId, attempt, rows.GetString(3));
            }
        }
        if (existing)
        {
            await using var verify = Query("""
                SELECT EXISTS(SELECT 1 FROM organization_deletion_progress WHERE tenant_id=@tenant AND request_id=@request)
                 AND EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id=@tenant AND job_type=@type
                  AND idempotency_key=@key AND actor_id=@actor AND service_identity=@service
                  AND correlation_id=@correlation AND safe_metadata=@metadata);
                """);
            verify.Parameters.AddWithValue("type", job.JobType); verify.Parameters.AddWithValue("key", job.IdempotencyKey);
            verify.Parameters.AddWithValue("service", job.ServiceIdentity); verify.Parameters.AddWithValue("correlation", job.CorrelationId);
            verify.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, job.SafeMetadataJson);
            if (await verify.ExecuteScalarAsync(cancellationToken) is not true) throw Unavailable();
            return false;
        }
        await using (var create = Query("""
            INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id)
             VALUES(@tenant,@request,@actor,@version,@correlation);
            INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase)
             VALUES(@tenant,@request,@request,'ATTACHMENTS');
            """))
        {
            create.Parameters.AddWithValue("correlation", job.CorrelationId);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await jobs.PublishAsync(session, job, cancellationToken)) throw Unavailable();
        // Borrowed transaction: only the owning command may commit all effects.
        return true;
    }
    private static OrganizationDeletionPublicationUnavailableException Unavailable() => new();
}
