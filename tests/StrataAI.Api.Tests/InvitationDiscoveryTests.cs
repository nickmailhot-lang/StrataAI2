using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // ONBOARD-FR-004/005/009, PRD-60-TC-04/07/15: verified account discovery and natural-ID retry.
    [Theory]
    [InlineData("INTERNAL", "MEMBER")]
    [InlineData("PORTAL", "OWNER")]
    public async Task Verified_account_discovers_and_accepts_its_own_invitation_without_a_mail_token(string surface, string role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var wrong = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(recipient); await RegisterAndLogin(wrong);
        var user = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invitation council" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var issued = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", new { email = user.GetProperty("email").GetString(), surface, targetRole = role });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var invite = await issued.Content.ReadFromJsonAsync<JsonElement>(ct); var id = invite.GetProperty("id").GetGuid();
        var page = await recipient.GetFromJsonAsync<JsonElement>("/me/invitations", ct);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetGuid()); Assert.Equal(surface, item.GetProperty("surface").GetString());
        Assert.Equal("Invitation council", item.GetProperty("organizationName").GetString());
        Assert.DoesNotContain("token", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var wrongPage = await wrong.GetFromJsonAsync<JsonElement>("/me/invitations", ct);
        Assert.Empty(wrongPage.GetProperty("items").EnumerateArray());
        using var denied = await Mutate(wrong, HttpMethod.Post, $"/me/invitations/{id}/accept", new { });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var replies = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(recipient, HttpMethod.Post, $"/me/invitations/{id}/accept", new { })));
        foreach (var reply in replies)
        {
            using (reply) { Assert.Equal(HttpStatusCode.OK, reply.StatusCode); Assert.Equal(id, (await reply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("invitationId").GetGuid()); }
        }
        var membership = await app.Services.GetRequiredService<IOrganizationStore>().FindMembershipAsync(org, user.GetProperty("id").GetGuid(), ct);
        if (surface == "PORTAL") Assert.Null(membership);
        else { Assert.NotNull(membership); Assert.Equal(OrganizationRole.Member, membership.Role); Assert.Equal(1, membership.Version); }
        var stored = await app.Services.GetRequiredService<IInvitationStore>().FindActiveByIdForEmailAsync(id, user.GetProperty("id").GetGuid(), user.GetProperty("email").GetString()!.ToUpperInvariant(), DateTimeOffset.UtcNow, ct);
        Assert.Equal(user.GetProperty("id").GetGuid(), stored!.AcceptedByUserId);
        Assert.Empty((await recipient.GetFromJsonAsync<JsonElement>("/me/invitations", ct)).GetProperty("items").EnumerateArray());
        using var tokenReuse = await Mutate(recipient, HttpMethod.Post, $"/invitations/{invite.GetProperty("invitationToken").GetString()}/accept", new { });
        Assert.Equal(HttpStatusCode.BadRequest, tokenReuse.StatusCode);
        using var logout = await Mutate(recipient, HttpMethod.Post, "/auth/logout", new { });
        using var revoked = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{id}/accept", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Pending_invitation_pages_are_bounded_and_invalid_cursors_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(recipient);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var email = (await recipient.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("email").GetString()!;
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Paged invitations" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var service = app.Services.GetRequiredService<IInvitationService>();
        for (var i = 0; i < 101; i++) Assert.True((await service.CreateAsync(org, ownerId, email, InvitationSurface.Internal, "MEMBER", "pagination-fixture", ct)).Succeeded);
        var ids = new HashSet<Guid>(); string? cursor = null;
        foreach (var count in new[] { 50, 50, 1 })
        {
            var page = await recipient.GetFromJsonAsync<JsonElement>(cursor is null ? "/me/invitations" : $"/me/invitations?after={cursor}", ct);
            Assert.Equal(count, page.GetProperty("items").GetArrayLength());
            foreach (var item in page.GetProperty("items").EnumerateArray()) Assert.True(ids.Add(item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
        }
        Assert.Equal(101, ids.Count); Assert.Null(cursor);
        foreach (var invalid in new[] { "garbage", Guid.Empty.ToString(), "one%20OR%201=1" })
        { using var response = await recipient.GetAsync($"/me/invitations?after={invalid}", ct); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        using var denied = await anonymous.GetAsync("/me/invitations", ct); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Newly_verified_account_discovers_existing_invitation_without_administrator_repair()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        await RegisterAndLogin(owner);
        var id = Guid.NewGuid(); var email = $"unverified-{id:N}@example.test"; var now = DateTimeOffset.UtcNow;
        var tokens = app.Services.GetRequiredService<ISecureTokenService>(); var proof = tokens.Generate();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var user = new UserIdentity(id, email, email.ToUpperInvariant(), "Newly verified", null, "en-CA", "UTC", AccountStatus.Active, false,
            app.Services.GetRequiredService<IPasswordHashService>().Hash(id, "newly-verified-correct-horse"), now, now, 1);
        Assert.True(await identities.TryCreateUserAsync(user, new SecurityTokenRecord(Guid.NewGuid(), id, tokens.Hash(proof), now, now.AddMinutes(10)), null, ct));
        using var login = await Mutate(recipient, HttpMethod.Post, "/auth/login", new { email, password = "newly-verified-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Before verification" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var issued = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", new { email, surface = "INTERNAL", targetRole = "MEMBER" });
        var invitationId = (await issued.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var hidden = await recipient.GetAsync("/me/invitations", ct); Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        using var denied = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{invitationId}/accept", new { }); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.True(await identities.VerifyEmailAsync(tokens.Hash(proof), now, ct));
        var page = await recipient.GetFromJsonAsync<JsonElement>("/me/invitations", ct);
        Assert.Equal(invitationId, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var accepted = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{invitationId}/accept", new { }); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Accepted_invitation_acknowledgment_rechecks_current_issuer_grants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var admin = app.CreateClient(); using var recipient = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(admin); await RegisterAndLogin(recipient);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var user = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Issuer lifecycle" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var memberships = app.Services.GetRequiredService<IOrganizationStore>();
        await memberships.AddOrRestoreMemberAsync(org, adminId, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var issued = await app.Services.GetRequiredService<IInvitationService>().CreateAsync(org, adminId, user.GetProperty("email").GetString()!, InvitationSurface.Internal, "MEMBER", "issuer-fixture", ct);
        var id = issued.Value!.Invitation.Id;
        using var accepted = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{id}/accept", new { }); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True((await app.Services.GetRequiredService<IOrganizationService>().RemoveMemberAsync(org, ownerId, adminId, "issuer-fixture", ct)).Succeeded);
        using var denied = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{id}/accept", new { }); Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal(1, (await memberships.FindMembershipAsync(org, user.GetProperty("id").GetGuid(), ct))!.Version);
    }
}
