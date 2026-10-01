using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("INTERNAL", "MEMBER")]
    [InlineData("PORTAL", "OWNER")]
    public async Task Closed_registration_accepts_bound_invitation_without_premature_membership_and_retries_same_intent(string surface, string role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton(new IdentityPolicy(false, true, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30))));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var ownerId = Guid.NewGuid(); var ownerEmail = $"signup-owner-{ownerId:N}@example.test";
        var now = DateTimeOffset.UtcNow; const string password = "invitation-signup-correct-horse";
        Assert.True(await identities.TryCreateUserAsync(new UserIdentity(ownerId, ownerEmail, ownerEmail.ToUpperInvariant(), "Signup owner", null, "en-CA", "UTC",
            AccountStatus.Active, true, app.Services.GetRequiredService<IPasswordHashService>().Hash(ownerId, password), now, now, 1), null, null, ct));
        using var login = await Mutate(owner, HttpMethod.Post, "/auth/login", new { email = ownerEmail, password }); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invitation signup council" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var email = $"signup-recipient-{Guid.NewGuid():N}@example.test";
        using var issued = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", new { email, surface, targetRole = role });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var invitation = await issued.Content.ReadFromJsonAsync<JsonElement>(ct); var token = invitation.GetProperty("invitationToken").GetString()!;
        using var closed = await Mutate(recipient, HttpMethod.Post, "/auth/register", new { email, password, displayName = "Invited recipient" });
        Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        using var wrong = await Mutate(recipient, HttpMethod.Post, "/auth/register", new { email = $"wrong-{email}", password, displayName = "Invited recipient", invitationToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode); Assert.Null(await identities.FindUserByNormalizedEmailAsync($"wrong-{email}".ToUpperInvariant(), ct));
        var input = new { email, password, displayName = "Invited recipient", invitationToken = token };
        var key = Guid.NewGuid().ToString();
        using var registered = await Mutate(recipient, HttpMethod.Post, "/auth/register", input, key);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var first = await registered.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.DoesNotContain(token, first.GetRawText(), StringComparison.Ordinal);
        Assert.False(first.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        var recipientId = first.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(AccountStatus.PendingVerification, (await identities.FindUserByIdAsync(recipientId, ct))!.Status);
        Assert.Null(await app.Services.GetRequiredService<IOrganizationStore>().FindMembershipAsync(org, recipientId, ct));
        Assert.NotNull(await app.Services.GetRequiredService<IInvitationStore>().FindActiveByTokenHashAsync(app.Services.GetRequiredService<ISecureTokenService>().Hash(token), DateTimeOffset.UtcNow, ct));
        using var retry = await Mutate(recipient, HttpMethod.Post, "/auth/register", input, key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode); Assert.Equal(first.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        using var another = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", new { email, surface, targetRole = role });
        var otherToken = (await another.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("invitationToken").GetString();
        using var changed = await Mutate(recipient, HttpMethod.Post, "/auth/register", new { email, password, displayName = "Invited recipient", invitationToken = otherToken }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var verified = await Mutate(recipient, HttpMethod.Post, "/auth/verify-email", new { token = first.GetProperty("verificationToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var recipientLogin = await Mutate(recipient, HttpMethod.Post, "/auth/login", new { email, password }); Assert.Equal(HttpStatusCode.OK, recipientLogin.StatusCode);
        var invitationId = invitation.GetProperty("id").GetGuid();
        using var accepted = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{invitationId}/accept", new { }); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var membership = await app.Services.GetRequiredService<IOrganizationStore>().FindMembershipAsync(org, recipientId, ct);
        if (surface == "PORTAL") Assert.Null(membership); else Assert.True(membership!.Active);
        using var consumedSignup = await Mutate(recipient, HttpMethod.Post, "/auth/register", input, key); Assert.Equal(HttpStatusCode.BadRequest, consumedSignup.StatusCode);
    }
}
