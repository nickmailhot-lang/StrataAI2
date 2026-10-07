using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Onboarding;

public sealed class PostgresInvitationRecipientAuthorityDeliveryStore(PostgresConnectionFactory connections)
    : IInvitationRecipientAuthorityDeliveryStore
{
    public async Task<bool> DeliverNextPageAsync(ClaimedBackgroundJob job, Guid sourceEventId, int candidateLimit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT public.deliver_invitation_recipient_authority(@tenant,@job,@actor,@worker,@lease,@event,@limit)",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", job.OrganizationId);
        command.Parameters.AddWithValue("job", job.Id);
        command.Parameters.AddWithValue("actor", job.ActorId);
        command.Parameters.AddWithValue("worker", job.WorkerId);
        command.Parameters.AddWithValue("lease", job.LeaseId);
        command.Parameters.AddWithValue("event", sourceEventId);
        command.Parameters.AddWithValue("limit", candidateLimit);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true) return false;
        await session.CommitAsync(cancellationToken);
        return true;
    }
}
