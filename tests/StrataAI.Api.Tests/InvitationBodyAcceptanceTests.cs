using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("issuer_removed")]
    [InlineData("invitation_revoked")]
    [InlineData("invitation_expired")]
    public async Task Invitation_preview_rechecks_lifecycle_without_granting_access(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var admin = app.CreateClient(); using var recipient = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(admin); await RegisterAndLogin(recipient);
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var user = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Private preview council" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var members = app.Services.GetRequiredService<IOrganizationStore>();
        await members.AddOrRestoreMemberAsync(org, adminId, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var issued = await app.Services.GetRequiredService<IInvitationService>().CreateAsync(org, adminId,
            user.GetProperty("email").GetString()!, InvitationSurface.Internal, "MEMBER", "preview-fixture", ct);
        Assert.True(issued.Succeeded); var token = issued.Value!.RawToken;
        using var initial = await Mutate(recipient, HttpMethod.Post, "/invitations/review", new { token });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        if (change == "issuer_removed")
            Assert.Equal(OrganizationRemoveMemberResult.Removed, await members.RemoveMemberAsync(org, adminId, DateTimeOffset.UtcNow, ct));
        else if (change == "invitation_revoked")
            Assert.True(await invitations.RevokeAsync(org, issued.Value.Invitation.Id, DateTimeOffset.UtcNow, ct));
        else
        {
            var tokens = app.Services.GetRequiredService<ISecureTokenService>(); token = tokens.Generate();
            await invitations.CreateAsync(issued.Value.Invitation with { Id = Guid.NewGuid(), TokenHash = tokens.Hash(token),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-8), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }, ct);
        }
        using var denied = await Mutate(recipient, HttpMethod.Post, "/invitations/review", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.DoesNotContain("Private preview council", await denied.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Null(await members.FindMembershipAsync(org, user.GetProperty("id").GetGuid(), ct));
    }

    [Theory]
    [InlineData("INTERNAL", "MEMBER")]
    [InlineData("PORTAL", "OWNER")]
    public async Task Body_token_acceptance_binds_verified_recipient_and_does_not_restore_removed_access(string surface, string role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var wrong = app.CreateClient();
        using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(recipient); await RegisterAndLogin(wrong);
        var user = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Body invitation" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var issued = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations",
            new { email = user.GetProperty("email").GetString(), surface, targetRole = role });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var invitation = await issued.Content.ReadFromJsonAsync<JsonElement>(ct);
        var token = invitation.GetProperty("invitationToken").GetString()!;
        using var anonymousReview = await Mutate(anonymous, HttpMethod.Post, "/invitations/review", new { token });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousReview.StatusCode);
        using var wrongReview = await Mutate(wrong, HttpMethod.Post, "/invitations/review", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, wrongReview.StatusCode);
        using var reviewed = await Mutate(recipient, HttpMethod.Post, "/invitations/review", new { token });
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        var summary = await reviewed.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(invitation.GetProperty("id").GetGuid(), summary.GetProperty("id").GetGuid());
        Assert.Equal("Body invitation", summary.GetProperty("organizationName").GetString());
        Assert.Equal(surface, summary.GetProperty("surface").GetString());
        Assert.DoesNotContain(token, summary.GetRawText(), StringComparison.Ordinal);
        Assert.Null(await app.Services.GetRequiredService<IOrganizationStore>().FindMembershipAsync(org, user.GetProperty("id").GetGuid(), ct));
        using var unauthenticated = await Mutate(anonymous, HttpMethod.Post, "/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var mismatched = await Mutate(wrong, HttpMethod.Post, "/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        using var accepted = await Mutate(recipient, HttpMethod.Post, "/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var ack = await accepted.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(invitation.GetProperty("id").GetGuid(), ack.GetProperty("invitationId").GetGuid());
        Assert.Equal(org, ack.GetProperty("organizationId").GetGuid());
        Assert.Equal(surface, ack.GetProperty("surface").GetString());
        Assert.Equal(role, ack.GetProperty("targetRole").GetString());
        Assert.DoesNotContain(token, ack.GetRawText(), StringComparison.Ordinal);
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var member = await store.FindMembershipAsync(org, user.GetProperty("id").GetGuid(), ct);
        if (surface == "PORTAL") Assert.Null(member);
        else
        {
            Assert.NotNull(member);
            using var removed = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{member.UserId}", new { });
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }
        using var replay = await Mutate(recipient, HttpMethod.Post, "/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var consumedReview = await Mutate(recipient, HttpMethod.Post, "/invitations/review", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, consumedReview.StatusCode);
        if (surface == "INTERNAL")
            Assert.False((await store.FindMembershipAsync(org, user.GetProperty("id").GetGuid(), ct))?.Active ?? false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid-token")]
    public async Task Body_token_acceptance_rejects_invalid_proof_without_echoing_it(string? token)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var response = await Mutate(client, HttpMethod.Post, "/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("invalid_or_expired_invitation", body, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(token)) Assert.DoesNotContain(token, body, StringComparison.Ordinal);
    }
}
