namespace StrataAI.Application.Identity;

// Coordinates only. The registration receipt HMAC binds the token hash, never its bearer.
public sealed record InvitationRegistrationProof(Guid OrganizationId, Guid InvitationId, Guid IssuerId, string TokenHash);

public interface IInvitationRegistrationProofStore
{
    // Persistent transactions can roll back account/audit/receipt writes if expiry occurs during a wait.
    bool RequiresFinalCheck { get; }
    Task<InvitationRegistrationProof?> PrepareAsync(string tokenHash, string emailNormalized, CancellationToken cancellationToken);
    Task<bool> CheckAsync(InvitationRegistrationProof proof, string emailNormalized, CancellationToken cancellationToken);
}
