using Npgsql;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Onboarding;

public sealed class PostgresInvitationRecipientAuthorityDependencyPublisher(PostgresConnectionFactory connections)
    : IInvitationRecipientAuthorityDependencyPublisher
{
    public async Task<bool> BindAsync(Guid tenantId, Guid actorId, Guid invitationId, Guid sourceEventId,
        CancellationToken cancellationToken)
    {
        if (!connections.HasCommandScope(tenantId))
            throw new InvalidOperationException("Recipient dependency requires its owning acceptance transaction.");
        await using var session = await connections.OpenTenantSessionAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT bind_invitation_recipient_authority_dependency(@tenant,@actor,@invitation,@source);",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenantId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("invitation", invitationId); command.Parameters.AddWithValue("source", sourceEventId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
