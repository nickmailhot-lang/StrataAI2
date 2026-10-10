namespace StrataAI.Application.Onboarding;

// Bind the actual membership source inside its owning acceptance transaction.
// Caller-provided correlation IDs cannot establish this causal relationship.
public interface IInvitationRecipientAuthorityDependencyPublisher
{
    Task<bool> BindAsync(Guid tenantId, Guid actorId, Guid invitationId, Guid sourceEventId,
        CancellationToken cancellationToken);
}
