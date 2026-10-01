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

    [Theory]
    [InlineData(BoardRole.Admin)]
    [InlineData(BoardRole.Member)]
    public async Task Board_creation_route_returns_bound_token_free_retry_ack_and_rechecks_revoked_issuer(BoardRole role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        const string password = "board-route-correct-horse";
        await app.Services.GetRequiredService<IIdentityStore>().UpdatePasswordHashAsync(fixture.Inviter.Id,
            app.Services.GetRequiredService<IPasswordHashService>().Hash(fixture.Inviter.Id, password), DateTimeOffset.UtcNow, ct);
        using var client = app.CreateClient();
        using var login = await Mutate(client, HttpMethod.Post, "/auth/login", new { email = fixture.Inviter.Email, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, login.StatusCode);
        var key = Guid.NewGuid();
        async Task<HttpResponseMessage> Create(string requestedRole)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/boards/{fixture.Board.Id}/invitations") {
                Content = System.Net.Http.Json.JsonContent.Create(new { email = fixture.Recipient.Email, role = requestedRole }) };
            request.Headers.Add("X-StrataAI-Request", "1"); request.Headers.Add("Idempotency-Key", key.ToString());
            return await client.SendAsync(request, ct);
        }
        using var created = await Create(role.ToString().ToUpperInvariant());
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadAsStringAsync(ct);
        using var parsed = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(fixture.Board.Id, parsed.RootElement.GetProperty("boardTarget").GetProperty("boardId").GetGuid());
        Assert.Equal(role.ToString().ToUpperInvariant(), parsed.RootElement.GetProperty("boardTarget").GetProperty("role").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, parsed.RootElement.GetProperty("invitationToken").ValueKind);
        using var retry = await Create(role.ToString().ToUpperInvariant());
        Assert.Equal(System.Net.HttpStatusCode.Created, retry.StatusCode); Assert.Equal(body, await retry.Content.ReadAsStringAsync(ct));
        using var conflict = await Create(role == BoardRole.Admin ? "MEMBER" : "ADMIN");
        Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
        using var invalid = await Create("OWNER"); Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
        var otherBoard = (await app.Services.GetRequiredService<IWorkManagementService>().CreateBoardAsync(fixture.Board.OrganizationId,
            fixture.Owner.Id, "Other private Board", null, BoardVisibility.Private, "COLOR", "#112233", "fixture", ct)).Value!;
        var otherInvitation = (await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(otherBoard.Id,
            fixture.Owner.Id, fixture.Recipient.Email, role, "fixture", ct)).Value!.Invitation.Id;
        var ordinaryInvitation = (await app.Services.GetRequiredService<IInvitationService>().CreateAsync(fixture.Board.OrganizationId,
            fixture.Owner.Id, fixture.Recipient.Email, InvitationSurface.Internal, "MEMBER", "fixture", ct)).Value!.Invitation.Id;
        using var history = await client.GetAsync($"/boards/{fixture.Board.Id}/invitations", ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, history.StatusCode);
        var historyBody = await history.Content.ReadAsStringAsync(ct);
        using var historyJson = System.Text.Json.JsonDocument.Parse(historyBody);
        var entry = Assert.Single(historyJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(parsed.RootElement.GetProperty("id").GetGuid(), entry.GetProperty("id").GetGuid());
        Assert.Equal(fixture.Recipient.Email, entry.GetProperty("email").GetString());
        Assert.Equal(fixture.Board.Id, entry.GetProperty("boardTarget").GetProperty("boardId").GetGuid());
        Assert.DoesNotContain(otherInvitation.ToString(), historyBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ordinaryInvitation.ToString(), historyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("tokenHash", historyBody, StringComparison.Ordinal);
        using var otherHistory = await client.GetAsync($"/boards/{otherBoard.Id}/invitations", ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, otherHistory.StatusCode);
        var invitationId = parsed.RootElement.GetProperty("id").GetGuid();
        using var wrongBoardRevoke = await Mutate(client, HttpMethod.Delete, $"/boards/{fixture.Board.Id}/invitations/{otherInvitation}", new { });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrongBoardRevoke.StatusCode);
        using var ordinaryRevoke = await Mutate(client, HttpMethod.Delete, $"/boards/{fixture.Board.Id}/invitations/{ordinaryInvitation}", new { });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ordinaryRevoke.StatusCode);
        using var revokedInvitation = await Mutate(client, HttpMethod.Delete, $"/boards/{fixture.Board.Id}/invitations/{invitationId}", new { });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, revokedInvitation.StatusCode);
        using var revokeRetry = await Mutate(client, HttpMethod.Delete, $"/boards/{fixture.Board.Id}/invitations/{invitationId}", new { });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, revokeRetry.StatusCode);
        Assert.Equal("invalid_or_expired_invitation", (await app.Services.GetRequiredService<IInvitationService>()
            .AcceptPendingAsync(fixture.Recipient.Id, invitationId, "fixture", ct)).ErrorCode);
        var stream = await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(fixture.Board.OrganizationId, fixture.Board.Id, 0, 100, ct);
        Assert.Single(stream.Events, row => row.Event.EventType == "INVITATION_REVOKED");
        Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().RemoveBoardMemberAsync(fixture.Board.Id,
            fixture.Owner.Id, fixture.Inviter.Id, "fixture", ct)).Succeeded);
        using var revokeAfterRemoval = await Mutate(client, HttpMethod.Delete, $"/boards/{fixture.Board.Id}/invitations/{invitationId}", new { });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, revokeAfterRemoval.StatusCode);
        using var revokedHistory = await client.GetAsync($"/boards/{fixture.Board.Id}/invitations", ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, revokedHistory.StatusCode);
        Assert.DoesNotContain(fixture.Recipient.Email, await revokedHistory.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        using var revoked = await Create(role.ToString().ToUpperInvariant()); Assert.Equal(System.Net.HttpStatusCode.NotFound, revoked.StatusCode);
        using var hidden = await Create("OWNER"); Assert.Equal(System.Net.HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Null(await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, ct));
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
