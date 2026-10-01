namespace StrataAI.Application.Onboarding;

public interface IInvitationService
{
    Task<InvitationOperation<CreatedInvitation>> CreateAsync(
        Guid organizationId,
        Guid actorUserId,
        string invitedEmail,
        InvitationSurface surface,
        string targetRole,
        string correlationId,
        CancellationToken cancellationToken = default,
        Guid? idempotencyKey = null);

    Task<InvitationOperation<PendingInvitationPage>> ListPendingAsync(
        Guid actorUserId,
        Guid? after = null,
        CancellationToken cancellationToken = default);

    Task<InvitationOperation<AcceptedInvitation>> AcceptPendingAsync(Guid actorUserId, Guid invitationId,
        string correlationId, CancellationToken cancellationToken = default);

    Task<InvitationOperation<AcceptedInvitation>> AcceptAsync(
        Guid actorUserId,
        string rawToken,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InvitationOperation<bool>> RevokeAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid invitationId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
