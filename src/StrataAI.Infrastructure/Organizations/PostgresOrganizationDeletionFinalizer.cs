using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationDeletionFinalizer(PostgresConnectionFactory connections) : IOrganizationDeletionFinalizer
{
    public async Task<bool> FinishAsync(ClaimedBackgroundJob job, OrganizationDeletionAttempt attempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (job.JobType != OrganizationDeletionJobs.Type || job.ServiceIdentity != OrganizationDeletionJobs.Service
            || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty || job.Id == Guid.Empty
            || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty || OrganizationDeletionAttempt.Parse(job.SafeMetadataJson) != attempt)
            return false;
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT public.finish_organization_deletion(@tenant,@job,@actor,@worker,@lease,@request,@step,@version)", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", job.OrganizationId); command.Parameters.AddWithValue("job", job.Id);
        command.Parameters.AddWithValue("actor", job.ActorId); command.Parameters.AddWithValue("worker", job.WorkerId);
        command.Parameters.AddWithValue("lease", job.LeaseId); command.Parameters.AddWithValue("request", attempt.RequestId);
        command.Parameters.AddWithValue("step", attempt.StepId); command.Parameters.AddWithValue("version", attempt.AcceptedVersion);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true) return false;
        await session.CommitAsync(cancellationToken);
        return true;
    }
}
