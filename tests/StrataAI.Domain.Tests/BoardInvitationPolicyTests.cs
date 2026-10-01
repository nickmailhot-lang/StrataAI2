using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class BoardInvitationPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
    private static readonly Guid OrganizationId = Guid.NewGuid(), BoardId = Guid.NewGuid();
    private static readonly UserIdentity Issuer = User(Guid.NewGuid()), Recipient = User(Guid.NewGuid());
    private static readonly OrganizationRecord Organization = new(OrganizationId, "Organization", null, null,
        Issuer.Id, OrganizationStatus.Active, Now, Now, 1);
    private static readonly BoardRecord Board = new(BoardId, OrganizationId, "Board", null,
        BoardVisibility.Private, "COLOR", null, BoardLifecycleState.Active, Now, Now, 1);
    private static readonly OrganizationMembership IssuerMembership = Membership(Issuer.Id, OrganizationRole.Member);
    private static readonly OrganizationMembership RecipientMembership = Membership(Recipient.Id, OrganizationRole.Member);
    private static readonly BoardMemberRecord BoardAdmin = new(BoardId, Issuer.Id, BoardRole.Admin, true, Now, Now, 1);

    [Theory]
    [InlineData(BoardRole.Member)]
    [InlineData(BoardRole.Admin)]
    public void Board_admin_invites_existing_eligible_member_without_Organization_enrollment(BoardRole role)
    {
        Assert.True(Issue(role: role));
        Assert.False(BoardInvitationPolicy.CanEnrollOrganizationMember(Organization, Board, Issuer,
            IssuerMembership, role, true));
    }

    [Theory]
    [InlineData(OrganizationRole.Owner)]
    [InlineData(OrganizationRole.Admin)]
    public void Organization_administration_can_invite_new_account_to_board(OrganizationRole role)
    {
        var membership = IssuerMembership with { Role = role };
        Assert.True(BoardInvitationPolicy.CanIssue(Organization, Board, Issuer, membership, null,
            null, null, BoardRole.Member, true));
        Assert.True(BoardInvitationPolicy.CanEnrollOrganizationMember(Organization, Board, Issuer,
            membership, BoardRole.Member, true));
    }

    [Fact]
    public void Board_admin_cannot_enroll_unknown_or_ineligible_recipient()
    {
        Assert.False(BoardInvitationPolicy.CanIssue(Organization, Board, Issuer, IssuerMembership,
            BoardAdmin, null, null, BoardRole.Member, true));
        Assert.False(Issue(recipientMembership: RecipientMembership with { Active = false }));
        Assert.False(Issue(recipientMembership: RecipientMembership with { OrganizationId = Guid.NewGuid() }));
        Assert.False(Issue(recipientMembership: RecipientMembership with { UserId = Guid.NewGuid() }));
        Assert.False(Issue(recipient: Recipient with { Status = AccountStatus.Suspended }));
        Assert.False(Issue(recipient: Recipient with { EmailVerified = false }));
    }

    [Fact]
    public void Current_parent_and_issuer_authority_are_required_after_revocation()
    {
        Assert.False(Issue(organization: Organization with { Status = OrganizationStatus.Archived }));
        Assert.False(Issue(board: Board with { LifecycleState = BoardLifecycleState.Archived }));
        Assert.False(Issue(board: Board with { LifecycleState = BoardLifecycleState.Deleted }));
        Assert.False(Issue(issuer: Issuer with { Status = AccountStatus.Deactivated }));
        Assert.False(Issue(issuer: Issuer with { EmailVerified = false }));
        Assert.False(Issue(issuerMembership: IssuerMembership with { Active = false }));
        Assert.False(Issue(boardMembership: BoardAdmin with { Active = false }));
        Assert.False(Issue(boardMembership: BoardAdmin with { Role = BoardRole.Member }));
    }

    [Fact]
    public void Cross_Organization_or_actor_substitution_never_confers_authority()
    {
        Assert.False(Issue(board: Board with { OrganizationId = Guid.NewGuid() }));
        Assert.False(Issue(issuerMembership: IssuerMembership with { OrganizationId = Guid.NewGuid(), Role = OrganizationRole.Owner }));
        Assert.False(Issue(issuerMembership: IssuerMembership with { UserId = Guid.NewGuid(), Role = OrganizationRole.Owner }));
        Assert.False(Issue(boardMembership: BoardAdmin with { BoardId = Guid.NewGuid() }));
        Assert.False(Issue(boardMembership: BoardAdmin with { UserId = Guid.NewGuid() }));
        Assert.False(Issue(role: (BoardRole)99));
        Assert.False(Issue(issuerMembership: IssuerMembership with { Role = (OrganizationRole)99 }));
    }

    [Fact]
    public void Public_visibility_does_not_confer_invitation_authority()
    {
        Assert.False(Issue(board: Board with { Visibility = BoardVisibility.Public },
            boardMembership: BoardAdmin with { Role = BoardRole.Member }));
    }

    [Fact]
    public void Verification_policy_does_not_relax_active_account_requirement()
    {
        Assert.True(Issue(issuer: Issuer with { EmailVerified = false },
            recipient: Recipient with { EmailVerified = false }, requireVerified: false));
        Assert.False(Issue(recipient: Recipient with { Status = AccountStatus.PendingVerification }, requireVerified: false));
        Assert.False(Issue(issuer: Issuer with { Status = AccountStatus.Suspended }, requireVerified: false));
    }

    private static bool Issue(OrganizationRecord? organization = null, BoardRecord? board = null,
        UserIdentity? issuer = null, OrganizationMembership? issuerMembership = null,
        BoardMemberRecord? boardMembership = null, UserIdentity? recipient = null,
        OrganizationMembership? recipientMembership = null, BoardRole role = BoardRole.Member,
        bool requireVerified = true) => BoardInvitationPolicy.CanIssue(organization ?? Organization,
        board ?? Board, issuer ?? Issuer, issuerMembership ?? IssuerMembership,
        boardMembership ?? BoardAdmin, recipient ?? Recipient, recipientMembership ?? RecipientMembership,
        role, requireVerified);

    private static OrganizationMembership Membership(Guid userId, OrganizationRole role) =>
        new(Guid.NewGuid(), OrganizationId, userId, role, true, Now, Now, 1);
    private static UserIdentity User(Guid id) => new(id, $"{id:N}@example.test", $"{id:N}@EXAMPLE.TEST",
        "User", null, "en", "UTC", AccountStatus.Active, true, "unused", Now, Now, 1);
}
