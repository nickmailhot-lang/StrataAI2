using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationConfigurationDeliveryStore(PostgresConnectionFactory connections) : IOrganizationConfigurationDeliveryStore
{
    public async Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT public.deliver_organization_configuration_event(@tenant,@job,@actor,@worker,@lease,@event)", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", job.OrganizationId); command.Parameters.AddWithValue("job", job.Id);
        command.Parameters.AddWithValue("actor", job.ActorId); command.Parameters.AddWithValue("worker", job.WorkerId);
        command.Parameters.AddWithValue("lease", job.LeaseId); command.Parameters.AddWithValue("event", eventId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true) return false;
        await session.CommitAsync(cancellationToken);
        return true;
    }
}
