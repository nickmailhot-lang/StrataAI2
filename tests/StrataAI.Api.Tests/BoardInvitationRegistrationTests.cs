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
    // PRD-60 ONBOARD-FR-001/002/004; TC-01/04/05/06/07/10.
    [Theory]
    [InlineData(BoardRole.Member)]
    [InlineData(BoardRole.Admin)]
    public async Task Closed_Board_signup_binds_email_and_retries_without_consuming_or_granting_access(BoardRole role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton(
            new IdentityPolicy(false, true, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30))));
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var email = $"board-signup-{Guid.NewGuid():N}@example.test";
        var invitation = (await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(
            fixture.Board.Id, fixture.Owner.Id, email, role, "fixture", ct)).Value!;
        using var client = app.CreateClient();
        const string password = "board-signup-correct-horse";
        using var wrong = await Mutate(client, HttpMethod.Post, "/auth/register", new {
            email = $"wrong-{email}", password, displayName = "Board recipient", invitationToken = invitation.RawToken });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var input = new { email, password, displayName = "Board recipient", invitationToken = invitation.RawToken };
        var key = Guid.NewGuid().ToString();
        using var registered = await Mutate(client, HttpMethod.Post, "/auth/register", input, key);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var first = await registered.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.DoesNotContain(invitation.RawToken, first.GetRawText(), StringComparison.Ordinal);
        var userId = first.GetProperty("user").GetProperty("id").GetGuid();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        Assert.Equal(AccountStatus.PendingVerification, (await identities.FindUserByIdAsync(userId, ct))!.Status);
        using var retry = await Mutate(client, HttpMethod.Post, "/auth/register", input, key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(first.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        Assert.Null(await organizations.FindMembershipAsync(fixture.Board.OrganizationId, userId, ct));
        Assert.Null(await work.FindBoardMemberAsync(fixture.Board.Id, userId, ct));
        using var verified = await Mutate(client, HttpMethod.Post, "/auth/verify-email", new {
            token = first.GetProperty("verificationToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.True((await identities.FindUserByIdAsync(userId, ct))!.EmailVerified);
        Assert.Null(await organizations.FindMembershipAsync(fixture.Board.OrganizationId, userId, ct));
        Assert.Null(await work.FindBoardMemberAsync(fixture.Board.Id, userId, ct));
        var canonical = await app.Services.GetRequiredService<IInvitationStore>().FindActiveByTokenHashAsync(
            app.Services.GetRequiredService<ISecureTokenService>().Hash(invitation.RawToken), DateTimeOffset.UtcNow, ct);
        Assert.Equal(new BoardInvitationTarget(fixture.Board.Id, role), canonical!.BoardTarget);
        Assert.Null(canonical.AcceptedAt);
    }

    [Theory]
    [InlineData("ARCHIVED")]
    [InlineData("REVOKED")]
    [InlineData("ISSUER_DEMOTED")]
    public async Task Board_signup_rechecks_current_parent_proof_and_Organization_enrollment_authority(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton(
            new IdentityPolicy(false, true, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30))));
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(fixture.Board.OrganizationId, fixture.Inviter.Id,
            OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var email = $"board-stale-signup-{Guid.NewGuid():N}@example.test";
        var invitation = (await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(
            fixture.Board.Id, fixture.Inviter.Id, email, BoardRole.Member, "fixture", ct)).Value!;
        if (change == "ARCHIVED")
            Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().ArchiveBoardAsync(
                fixture.Board.Id, fixture.Owner.Id, fixture.Board.Version, "fixture", ct)).Succeeded);
        else if (change == "REVOKED")
            Assert.True((await app.Services.GetRequiredService<IInvitationService>().RevokeAsync(
                fixture.Board.OrganizationId, fixture.Owner.Id, invitation.Invitation.Id, "fixture", ct)).Succeeded);
        else
            await organizations.AddOrRestoreMemberAsync(fixture.Board.OrganizationId, fixture.Inviter.Id,
                OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var client = app.CreateClient();
        using var denied = await Mutate(client, HttpMethod.Post, "/auth/register", new {
            email, password = "board-stale-correct-horse", displayName = "Board recipient", invitationToken = invitation.RawToken });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Null(await app.Services.GetRequiredService<IIdentityStore>().FindUserByNormalizedEmailAsync(email.ToUpperInvariant(), ct));
    }
}
