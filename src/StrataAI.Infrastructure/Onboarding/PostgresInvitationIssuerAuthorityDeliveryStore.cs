using Npgsql;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Onboarding;

public sealed class PostgresInvitationIssuerAuthorityDeliveryStore(PostgresConnectionFactory connections)
    : IInvitationIssuerAuthorityDeliveryStore
{
    public async Task<InvitationIssuerAuthorityClaim?> ClaimAsync(Guid workerId, CancellationToken cancellationToken)
    {
        if (workerId == Guid.Empty) throw new ArgumentException("Worker identity is required.", nameof(workerId));
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT * FROM public.claim_invitation_issuer_authority(@worker)", session.Connection);
        command.Parameters.AddWithValue("worker", workerId);
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        return await row.ReadAsync(cancellationToken)
            ? new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetGuid(3), row.GetGuid(4), row.GetString(5)) : null;
    }

    public async Task<bool> DeliverAsync(InvitationIssuerAuthorityClaim claim, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var transaction = await session.Connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT public.deliver_invitation_issuer_authority(@job,@event,@actor,@worker,@lease,@limit)", session.Connection, transaction);
        command.Parameters.AddWithValue("job", claim.JobId); command.Parameters.AddWithValue("event", claim.EventId);
        command.Parameters.AddWithValue("actor", claim.ActorId); command.Parameters.AddWithValue("worker", claim.WorkerId);
        command.Parameters.AddWithValue("lease", claim.LeaseId); command.Parameters.AddWithValue("limit", limit);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true) return false;
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
