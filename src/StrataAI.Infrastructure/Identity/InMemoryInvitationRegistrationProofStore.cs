using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.Identity;

internal sealed class InMemoryInvitationRegistrationProofStore(IInvitationStore invitations, IOrganizationStore organizations,
    IIdentityStore identities, IClock clock, IdentityPolicy policy, IWorkManagementStore work) : IInvitationRegistrationProofStore
{
    // Demo commands share the account/Organization gate but do not support write rollback.
    // Admission is checked before mutation; real post-write waits/expiry are verified in PostgreSQL CI.
    public bool RequiresFinalCheck => false;
    public async Task<InvitationRegistrationProof?> PrepareAsync(string tokenHash, string emailNormalized, CancellationToken ct)
    {
        var invitation = await invitations.FindActiveByTokenHashAsync(tokenHash, clock.UtcNow, ct);
        if (invitation is null) return null;
        var proof = new InvitationRegistrationProof(invitation.OrganizationId, invitation.Id, invitation.CreatedByUserId, tokenHash);
        return await CheckAsync(proof, emailNormalized, ct) ? proof : null;
    }
    public async Task<bool> CheckAsync(InvitationRegistrationProof proof, string emailNormalized, CancellationToken ct)
    {
        var invitation = await invitations.FindActiveByTokenHashAsync(proof.TokenHash, clock.UtcNow, ct);
        if (invitation is null || invitation.Id != proof.InvitationId || invitation.OrganizationId != proof.OrganizationId
            || invitation.CreatedByUserId != proof.IssuerId || invitation.EmailNormalized != emailNormalized) return false;
        var organization = await organizations.FindOrganizationAsync(proof.OrganizationId, ct);
        var member = await organizations.FindMembershipAsync(proof.OrganizationId, proof.IssuerId, ct);
        var issuer = await identities.FindUserByIdAsync(proof.IssuerId, ct);
        if (invitation.BoardTarget is { } target)
        {
            var board = await work.FindBoardAsync(target.BoardId, ct);
            if (board is not { LifecycleState: BoardLifecycleState.Active } || board.OrganizationId != proof.OrganizationId
                || !Enum.IsDefined(target.Role) || invitation.Surface != InvitationSurface.Internal || invitation.TargetRole != "MEMBER")
                return false;
        }
        return organization is { Status: OrganizationStatus.Active }
            && member is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            && issuer is { Status: AccountStatus.Active } && (!policy.RequireVerifiedEmail || issuer.EmailVerified)
            && (invitation.Surface == InvitationSurface.Internal
                ? invitation.TargetRole is "OWNER" or "ADMIN" or "MEMBER"
                : invitation.Surface == InvitationSurface.Portal && invitation.TargetRole is "OWNER" or "CO_OWNER" or "TENANT" or "OCCUPANT" or "AUTHORIZED_REPRESENTATIVE" or "OTHER")
            && !(invitation.Surface == InvitationSurface.Internal && invitation.TargetRole == "OWNER" && member.Role != OrganizationRole.Owner)
            && invitation.ExpiresAt > clock.UtcNow && invitation.AcceptedAt is null && invitation.RevokedAt is null;
    }
}
