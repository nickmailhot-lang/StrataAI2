namespace StrataAI.Application.Onboarding;

public sealed record InvitationDeliveryToken(string RawToken, string KeyId);

public interface IInvitationMailPublisher
{
    InvitationDeliveryToken CreateToken(Guid organizationId, Guid invitationId);
    // Requires the existing authorized Organization transaction. Publication
    // must commit/roll back with invitation, audit and creation receipt.
    Task PublishAsync(InvitationRecord invitation, string keyId, string correlationId, CancellationToken cancellationToken);
}
