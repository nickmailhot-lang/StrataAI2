using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationDeletionPageStore(PostgresConnectionFactory connections) : IOrganizationDeletionPageStore
{
    public async Task<bool> ApplyPageAsync(ClaimedBackgroundJob job, OrganizationDeletionAttempt attempt, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > OrganizationDeletionJobs.PageSize || job.OrganizationId == Guid.Empty
            || job.JobType != OrganizationDeletionJobs.Type || job.ServiceIdentity != OrganizationDeletionJobs.Service
            || OrganizationDeletionAttempt.Parse(job.SafeMetadataJson) != attempt) return false;
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId,cancellationToken);
        await using var command = new NpgsqlCommand("SELECT public.apply_organization_deletion_page(@tenant,@job,@actor,@worker,@lease,@request,@step,@version,@limit)",session.Connection,session.Transaction);
        command.Parameters.AddWithValue("tenant",job.OrganizationId); command.Parameters.AddWithValue("job",job.Id);
        command.Parameters.AddWithValue("actor",job.ActorId); command.Parameters.AddWithValue("worker",job.WorkerId);
        command.Parameters.AddWithValue("lease",job.LeaseId); command.Parameters.AddWithValue("request",attempt.RequestId);
        command.Parameters.AddWithValue("step",attempt.StepId); command.Parameters.AddWithValue("version",attempt.AcceptedVersion);
        command.Parameters.AddWithValue("limit",pageSize);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true) return false;
        await session.CommitAsync(cancellationToken); return true;
    }
}
