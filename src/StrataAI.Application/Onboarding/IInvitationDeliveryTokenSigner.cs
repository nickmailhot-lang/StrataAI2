namespace StrataAI.Application.Onboarding;

// API and Worker share only the validated runtime key ring. Durable publication
// retains these coordinates and the canonical token hash, never the bearer.
public interface IInvitationDeliveryTokenSigner
{
    string CurrentKeyId { get; }
    string DeriveInvitation(Guid organizationId, Guid invitationId, string keyId);
}
