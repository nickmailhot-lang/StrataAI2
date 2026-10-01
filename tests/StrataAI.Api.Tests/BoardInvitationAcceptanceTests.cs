using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    // PRD-05 PERM-FR-004/005/006; PRD-60 ONBOARD-FR-003/004/009, TC-01/04/06/07.
    [Theory]
    [InlineData(BoardRole.Member)]
    [InlineData(BoardRole.Admin)]
    public async Task Board_body_acceptance_grants_bound_role_and_retry_never_restores_removed_membership(BoardRole role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var issued = (await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(fixture.Board.Id,
            fixture.Inviter.Id, fixture.Recipient.Email, role, "fixture", ct)).Value!;
        var service = app.Services.GetRequiredService<IInvitationService>();
        Assert.Equal("invalid_or_expired_invitation", (await service.AcceptAsync(fixture.Owner.Id, issued.RawToken, "fixture", ct)).ErrorCode);
        const string password = "board-accept-correct-horse";
        await app.Services.GetRequiredService<IIdentityStore>().UpdatePasswordHashAsync(fixture.Recipient.Id,
            app.Services.GetRequiredService<IPasswordHashService>().Hash(fixture.Recipient.Id, password), DateTimeOffset.UtcNow, ct);
        using var client = app.CreateClient();
        using var login = await Mutate(client, HttpMethod.Post, "/auth/login", new { email = fixture.Recipient.Email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var accepted = await Mutate(client, HttpMethod.Post, "/invitations/accept", new { token = issued.RawToken });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var ack = await accepted.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(fixture.Board.Id, ack.GetProperty("boardTarget").GetProperty("boardId").GetGuid());
        Assert.Equal(role.ToString().ToUpperInvariant(), ack.GetProperty("boardTarget").GetProperty("role").GetString());
        Assert.DoesNotContain(issued.RawToken, ack.GetRawText(), StringComparison.Ordinal);
        var workStore = app.Services.GetRequiredService<IWorkManagementStore>();
        Assert.Equal(role, (await workStore.FindBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, ct))!.Role);
        Assert.Equal(OrganizationRole.Member, (await app.Services.GetRequiredService<IOrganizationStore>()
            .FindMembershipAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, ct))!.Role);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        Assert.True((await work.GetBoardAsync(fixture.Board.Id, fixture.Recipient.Id, ct)).Value!.Access.CanEdit);
        using var used = await Mutate(client, HttpMethod.Post, "/invitations/accept", new { token = issued.RawToken });
        Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
        using var retry = await Mutate(client, HttpMethod.Post, $"/me/invitations/{issued.Invitation.Id}/accept", new { });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(ack.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        Assert.True((await work.RemoveBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, "fixture", ct)).Succeeded);
        Assert.True((await service.AcceptPendingAsync(fixture.Recipient.Id, issued.Invitation.Id, "fixture", ct)).Succeeded);
        Assert.False((await workStore.FindBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, ct))!.Active);
        var page = await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(fixture.Board.OrganizationId,
            fixture.Board.Id, 0, 100, ct);
        Assert.Single(page.Events, row => row.Event.EventType == "BOARD_MEMBER_ADDED");
        Assert.Single(page.Events, row => row.Event.EventType == "INVITATION_ACCEPTED");
    }

    [Theory]
    [InlineData(OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Admin, false)]
    [InlineData(OrganizationRole.Member, true)]
    public async Task Board_invitation_preserves_current_Organization_role_and_active_Board_admin(OrganizationRole orgRole, bool boardAdmin)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, orgRole, DateTimeOffset.UtcNow, ct);
        if (boardAdmin) Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().SetBoardMemberAsync(
            fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, BoardRole.Admin, "fixture", ct)).Succeeded);
        var issued = (await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Recipient.Email, BoardRole.Member, "fixture", ct)).Value!;
        Assert.True((await app.Services.GetRequiredService<IInvitationService>().AcceptAsync(fixture.Recipient.Id, issued.RawToken, "fixture", ct)).Succeeded);
        Assert.Equal(orgRole, (await organizations.FindMembershipAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, ct))!.Role);
        Assert.Equal(boardAdmin ? BoardRole.Admin : BoardRole.Member, (await app.Services.GetRequiredService<IWorkManagementStore>()
            .FindBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, ct))!.Role);
    }

    [Fact]
    public async Task Board_enrollment_does_not_revive_roles_from_removed_Organization_membership()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(fixture.Board.OrganizationId,
            fixture.Recipient.Id, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().SetBoardMemberAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Recipient.Id, BoardRole.Admin, "fixture", ct)).Succeeded);
        Assert.True((await app.Services.GetRequiredService<IOrganizationService>().RemoveMemberAsync(fixture.Board.OrganizationId,
            fixture.Owner.Id, fixture.Recipient.Id, "fixture", ct)).Succeeded);
        var issued = (await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Recipient.Email, BoardRole.Member, "fixture", ct)).Value!;
        Assert.True((await app.Services.GetRequiredService<IInvitationService>().AcceptAsync(fixture.Recipient.Id, issued.RawToken, "fixture", ct)).Succeeded);
        Assert.Equal(OrganizationRole.Member, (await app.Services.GetRequiredService<IOrganizationStore>()
            .FindMembershipAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, ct))!.Role);
        Assert.Equal(BoardRole.Member, (await app.Services.GetRequiredService<IWorkManagementStore>()
            .FindBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, ct))!.Role);
    }

    [Theory]
    [InlineData("BOARD_ARCHIVED")]
    [InlineData("ISSUER_BOARD_REMOVED")]
    [InlineData("ISSUER_ORG_REMOVED")]
    [InlineData("RECIPIENT_ORG_REMOVED")]
    public async Task Board_acceptance_rechecks_current_scope_before_consumption(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var issued = (await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(fixture.Board.Id,
            fixture.Inviter.Id, fixture.Recipient.Email, BoardRole.Admin, "fixture", ct)).Value!;
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        if (change == "BOARD_ARCHIVED") Assert.True((await work.ArchiveBoardAsync(fixture.Board.Id, fixture.Owner.Id,
            fixture.Board.Version, "fixture", ct)).Succeeded);
        else if (change == "ISSUER_BOARD_REMOVED") Assert.True((await work.RemoveBoardMemberAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Inviter.Id, "fixture", ct)).Succeeded);
        else Assert.True((await app.Services.GetRequiredService<IOrganizationService>().RemoveMemberAsync(fixture.Board.OrganizationId,
            fixture.Owner.Id, change == "ISSUER_ORG_REMOVED" ? fixture.Inviter.Id : fixture.Recipient.Id, "fixture", ct)).Succeeded);
        Assert.Equal("invalid_or_expired_invitation", (await app.Services.GetRequiredService<IInvitationService>().AcceptAsync(
            fixture.Recipient.Id, issued.RawToken, "fixture", ct)).ErrorCode);
        Assert.Null(await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, ct));
        Assert.Null((await app.Services.GetRequiredService<IInvitationStore>().FindActiveByTokenHashAsync(
            app.Services.GetRequiredService<ISecureTokenService>().Hash(issued.RawToken), DateTimeOffset.UtcNow, ct))!.AcceptedAt);
    }
}
