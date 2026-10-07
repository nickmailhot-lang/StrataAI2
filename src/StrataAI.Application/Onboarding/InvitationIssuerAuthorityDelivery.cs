namespace StrataAI.Application.Onboarding;

// Global account source references only. Recipients and tenant routing remain
// behind the restricted database capability and never enter Worker messages.
public sealed record InvitationIssuerAuthorityClaim(Guid JobId, Guid EventId, Guid ActorId,
    Guid WorkerId, Guid LeaseId, string CorrelationId);

public interface IInvitationIssuerAuthorityDeliveryStore
{
    Task<InvitationIssuerAuthorityClaim?> ClaimAsync(Guid workerId, CancellationToken cancellationToken);
    // One exact lease projects at most 100 Organization roots and commits its
    // immutable continuation atomically. Recipient delivery remains separately leased.
    Task<bool> DeliverAsync(InvitationIssuerAuthorityClaim claim, int limit, CancellationToken cancellationToken);
}
