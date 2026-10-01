using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Onboarding;

/// <summary>
/// PRD-05 PERM-FR-004 and PRD-60 ONBOARD-FR-001/004. Evaluate current,
/// tenant-bound records inside the invitation command transaction, including on retry.
/// This policy does not grant access or replace session authorization.
/// </summary>
public static class BoardInvitationPolicy
{
    public static bool CanIssue(
        OrganizationRecord organization, BoardRecord board, UserIdentity issuer,
        OrganizationMembership? issuerMembership, BoardMemberRecord? issuerBoardMembership,
        UserIdentity? recipient, OrganizationMembership? recipientMembership,
        BoardRole targetRole, bool requireVerifiedEmail)
    {
        if (!Enum.IsDefined(targetRole)
            || organization.Status != OrganizationStatus.Active
            || board.LifecycleState != BoardLifecycleState.Active
            || board.OrganizationId != organization.Id
            || organization.Id == Guid.Empty || board.Id == Guid.Empty
            || !UsableAccount(issuer, requireVerifiedEmail)
            || !ActiveMembership(issuerMembership, organization.Id, issuer.Id))
            return false;

        // A Board administrator cannot enroll an outsider into the Organization.
        // Organization administrators may issue onboarding invitations; acceptance
        // still requires the recipient's current usable, verified account.
        if (issuerMembership!.Role is OrganizationRole.Owner or OrganizationRole.Admin)
            return true;

        return issuerMembership.Role == OrganizationRole.Member
            && issuerBoardMembership is { Active: true, Role: BoardRole.Admin }
            && issuerBoardMembership.BoardId == board.Id
            && issuerBoardMembership.UserId == issuer.Id
            && recipient is not null && UsableAccount(recipient, requireVerifiedEmail)
            && ActiveMembership(recipientMembership, organization.Id, recipient.Id);
    }

    public static bool CanEnrollOrganizationMember(
        OrganizationRecord organization, BoardRecord board, UserIdentity issuer,
        OrganizationMembership? issuerMembership, BoardRole targetRole, bool requireVerifiedEmail) =>
        issuerMembership is { Role: OrganizationRole.Owner or OrganizationRole.Admin }
        && CanIssue(organization, board, issuer, issuerMembership, null, null, null,
            targetRole, requireVerifiedEmail);

    private static bool ActiveMembership(OrganizationMembership? membership, Guid organizationId, Guid userId) =>
        membership is { Active: true }
        && Enum.IsDefined(membership.Role)
        && membership.OrganizationId == organizationId && membership.UserId == userId;

    private static bool UsableAccount(UserIdentity user, bool requireVerifiedEmail) =>
        user.Id != Guid.Empty && user.Status == AccountStatus.Active
        && (!requireVerifiedEmail || user.EmailVerified);
}
