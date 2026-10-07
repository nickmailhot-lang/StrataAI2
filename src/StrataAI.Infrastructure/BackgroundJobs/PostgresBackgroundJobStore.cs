using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.BackgroundJobs;

public sealed class PostgresBackgroundJobStore(PostgresConnectionFactory connections, bool previewJobs = false, bool metadataJobsOnly = false,
    bool authorityJobsOnly = false) : IBackgroundJobStore
{
    private readonly string _claimFunction = metadataJobsOnly && authorityJobsOnly
        ? throw new ArgumentException("Only one typed job claim scope is permitted.")
        : authorityJobsOnly ? "claim_invitation_recipient_authority_job"
        : metadataJobsOnly ? "claim_organization_metadata_job" : "claim_background_job";
    // Infrastructure producers pass their EXISTING domain transaction. This
    // method never opens/commits its own connection and cannot lose publication
    // independently of the domain write. Duplicate keys retain the first job.
    public async Task<bool> PublishAsync(TenantDbSession session, NewBackgroundJob job, CancellationToken cancellationToken = default)
    {
        if (session.OrganizationId != job.OrganizationId)
            throw new ArgumentException("Job Organization must match the transaction.", nameof(job));
        await using var command = new NpgsqlCommand("""
            INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,available_at)
            VALUES (@id,@tenant,@type,@key,@actor,@service,@correlation,@metadata,COALESCE(@available,clock_timestamp()))
            ON CONFLICT (tenant_id,job_type,idempotency_key) DO NOTHING;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("tenant", job.OrganizationId);
        command.Parameters.AddWithValue("type", job.JobType);
        command.Parameters.AddWithValue("key", job.IdempotencyKey);
        command.Parameters.AddWithValue("actor", job.ActorId);
        command.Parameters.AddWithValue("service", job.ServiceIdentity);
        command.Parameters.AddWithValue("correlation", job.CorrelationId);
        command.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, job.SafeMetadataJson);
        command.Parameters.AddWithValue("available", NpgsqlDbType.TimestampTz, (object?)job.AvailableAt?.ToUniversalTime() ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<ClaimedBackgroundJob?> ClaimAsync(Guid organizationId, Guid workerId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workerId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using (var capability = new NpgsqlCommand("SELECT set_config('app.attachment_preview_worker',@mode,true);", session.Connection, session.Transaction))
        {
            capability.Parameters.AddWithValue("mode",previewJobs ? "enabled" : "disabled");
            await capability.ExecuteNonQueryAsync(cancellationToken);
        }
        ClaimedBackgroundJob? job;
        await using (var command = new NpgsqlCommand($"""
            SELECT id,tenant_id,job_type,actor_id,service_identity,correlation_id,safe_metadata::text,
                   attempt_count,lease_id,worker_id,lease_expires_at FROM {_claimFunction}(@worker);
            """, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("worker", workerId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            job = await reader.ReadAsync(cancellationToken)
                ? new ClaimedBackgroundJob(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetGuid(8),
                    reader.GetGuid(9), reader.GetFieldValue<DateTimeOffset>(10)) : null;
        }
        await session.CommitAsync(cancellationToken);
        return job;
    }

    public Task<bool> CompleteAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, CancellationToken cancellationToken = default) =>
        FinishAsync(organizationId, jobId, leaseId, workerId, null, cancellationToken);

    public Task<bool> FailAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string errorCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return FinishAsync(organizationId, jobId, leaseId, workerId, errorCode, cancellationToken);
    }

    private async Task<bool> FinishAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string? errorCode, CancellationToken cancellationToken)
    {
        ValidateIdentity(workerId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand(errorCode is null
            ? "SELECT complete_background_job(@job,@lease,@worker);"
            : "SELECT fail_background_job(@job,@lease,@worker,@error);", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("lease", leaseId);
        command.Parameters.AddWithValue("worker", workerId);
        if (errorCode is not null) command.Parameters.AddWithValue("error", errorCode);
        var changed = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        await session.CommitAsync(cancellationToken);
        return changed;
    }

    private static void ValidateIdentity(Guid workerId)
    {
        if (workerId == Guid.Empty) throw new ArgumentException("Worker identity cannot be empty.", nameof(workerId));
    }
}
