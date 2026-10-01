using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(BoardRole.Member)]
    [InlineData(BoardRole.Admin)]
    public async Task Board_admin_creation_binds_target_and_retries_without_grant_or_duplicate_event(BoardRole role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var service = app.Services.GetRequiredService<BoardInvitationService>();
        var key = Guid.NewGuid();
        var replies = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => service.CreateAsync(fixture.Board.Id,
            fixture.Inviter.Id, fixture.Recipient.Email.ToLowerInvariant(), role, "fixture", ct, key)));
        Assert.All(replies, reply => Assert.True(reply.Succeeded));
        Assert.Equal(replies[0].Value!.Invitation.Id, replies[1].Value!.Invitation.Id);
        Assert.All(replies, reply => Assert.Equal("", reply.Value!.RawToken));
        var invitation = replies[0].Value!.Invitation;
        Assert.Equal(new BoardInvitationTarget(fixture.Board.Id, role), invitation.BoardTarget);
        Assert.Equal(fixture.Board.OrganizationId, invitation.OrganizationId);
        Assert.Equal(InvitationSurface.Internal, invitation.Surface);
        Assert.Equal("MEMBER", invitation.TargetRole);
        Assert.Equal(fixture.Recipient.EmailNormalized, invitation.EmailNormalized);
        Assert.Equal(fixture.Inviter.Id, invitation.CreatedByUserId);
        Assert.Null(await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(
            fixture.Board.Id, fixture.Recipient.Id, ct));
        Assert.Equal(OrganizationRole.Member, (await app.Services.GetRequiredService<IOrganizationStore>()
            .FindMembershipAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, ct))!.Role);
        var events = await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(
            fixture.Board.OrganizationId, fixture.Board.Id, 0, 100, ct);
        var change = Assert.Single(events.Events, row => row.Event.EventType == "BOARD_MEMBER_INVITED");
        Assert.Equal("Board", change.Event.EntityType);
        Assert.Equal(fixture.Board.Id, change.Event.EntityId);
        Assert.Equal(fixture.Inviter.Id, change.Event.ActorId);
        Assert.Equal("idempotency_key_reused", (await service.CreateAsync(fixture.Board.Id, fixture.Inviter.Id,
            fixture.Recipient.Email, role == BoardRole.Admin ? BoardRole.Member : BoardRole.Admin,
            "fixture", ct, key)).ErrorCode);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        Assert.True((await work.SetBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Inviter.Id,
            BoardRole.Member, "fixture", ct)).Succeeded);
        Assert.Equal("board_not_found", (await service.CreateAsync(fixture.Board.Id, fixture.Inviter.Id,
            fixture.Recipient.Email, role, "fixture", ct, key)).ErrorCode);
    }

    [Fact]
    public async Task Board_admin_cannot_enroll_outsider_or_replay_after_Organization_membership_removal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var service = app.Services.GetRequiredService<BoardInvitationService>();
        var key = Guid.NewGuid();
        Assert.Equal("board_not_found", (await service.CreateAsync(fixture.Board.Id, fixture.Inviter.Id,
            "new-board-outsider@example.test", BoardRole.Member, "fixture", ct, key)).ErrorCode);
        Assert.Null(await app.Services.GetRequiredService<IInvitationStore>().FindCreationReplayAsync(
            fixture.Board.OrganizationId, fixture.Inviter.Id, key, ct));
        Assert.True((await service.CreateAsync(fixture.Board.Id, fixture.Inviter.Id,
            fixture.Recipient.Email, BoardRole.Member, "fixture", ct, key)).Succeeded);
        Assert.True((await app.Services.GetRequiredService<IOrganizationService>().RemoveMemberAsync(
            fixture.Board.OrganizationId, fixture.Owner.Id, fixture.Inviter.Id, "fixture", ct)).Succeeded);
        Assert.Equal("board_not_found", (await service.CreateAsync(fixture.Board.Id, fixture.Inviter.Id,
            fixture.Recipient.Email, BoardRole.Member, "fixture", ct, key)).ErrorCode);
    }

    [Fact]
    public async Task Organization_owner_can_create_Board_onboarding_without_creating_account_or_grant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var email = $"new-board-{Guid.NewGuid():N}@example.test";
        var result = await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(fixture.Board.Id,
            fixture.Owner.Id, email, BoardRole.Admin, "fixture", ct);
        Assert.True(result.Succeeded);
        Assert.Equal(new BoardInvitationTarget(fixture.Board.Id, BoardRole.Admin), result.Value!.Invitation.BoardTarget);
        Assert.Equal("MEMBER", result.Value.Invitation.TargetRole);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RawToken));
        Assert.Equal(app.Services.GetRequiredService<ISecureTokenService>().Hash(result.Value.RawToken), result.Value.Invitation.TokenHash);
        Assert.Null(await app.Services.GetRequiredService<IIdentityStore>().FindUserByNormalizedEmailAsync(email.ToUpperInvariant(), ct));
    }

    [Fact]
    public async Task Board_invitation_rejects_invalid_input_and_archived_parent_before_creation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var service = app.Services.GetRequiredService<BoardInvitationService>();
        Assert.Equal("invalid_invitation_role", (await service.CreateAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Recipient.Email, (BoardRole)99, "fixture", ct)).ErrorCode);
        Assert.Equal("invalid_email", (await service.CreateAsync(fixture.Board.Id,
            fixture.Owner.Id, "not-an-email", BoardRole.Member, "fixture", ct)).ErrorCode);
        Assert.Equal("invalid_idempotency_key", (await service.CreateAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Recipient.Email, BoardRole.Member, "fixture", ct, Guid.Empty)).ErrorCode);
        Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().ArchiveBoardAsync(
            fixture.Board.Id, fixture.Owner.Id, fixture.Board.Version, "fixture", ct)).Succeeded);
        Assert.Equal("board_not_found", (await service.CreateAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Recipient.Email, BoardRole.Member, "fixture", ct)).ErrorCode);
    }

    private static async Task<(UserIdentity Owner, UserIdentity Inviter, UserIdentity Recipient, BoardRecord Board)>
        BoardInvitationFixtureAsync(ApiFactory app, CancellationToken ct)
    {
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        async Task<UserIdentity> User()
        {
            var id = Guid.NewGuid(); var email = $"board-command-{id:N}@example.test"; var now = DateTimeOffset.UtcNow;
            var user = new UserIdentity(id, email, email.ToUpperInvariant(), "Board command fixture", null, "en", "UTC",
                AccountStatus.Active, true, "unused-fixture-hash", now, now, 1);
            Assert.True(await identities.TryCreateUserAsync(user, null, null, ct)); return user;
        }
        var owner = await User(); var inviter = await User(); var recipient = await User();
        var org = (await app.Services.GetRequiredService<IOrganizationService>().CreateAsync(owner.Id,
            "Board command fixture", null, "fixture", ct)).Value!.Organization.Id;
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(org, inviter.Id, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        await organizations.AddOrRestoreMemberAsync(org, recipient.Id, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var board = (await work.CreateBoardAsync(org, owner.Id, "Private Board", null, BoardVisibility.Private,
            "COLOR", "#112233", "fixture", ct)).Value!;
        Assert.True((await work.SetBoardMemberAsync(board.Id, owner.Id, inviter.Id, BoardRole.Admin, "fixture", ct)).Succeeded);
        return (owner, inviter, recipient, board);
    }
}
