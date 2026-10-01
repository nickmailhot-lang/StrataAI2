using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD60_invitation_creation_retry_returns_same_token_free_ack_and_does_not_restore_revoked_invitation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invitation retry" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var input = new { email = "invite-retry@example.test", surface = "INTERNAL", targetRole = "ADMIN" };
        var route = $"/organizations/{org}/invitations";
        using var first = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var ack = await first.Content.ReadAsStringAsync(ct); var invitation = JsonSerializer.Deserialize<JsonElement>(ack);
        Assert.Equal(JsonValueKind.Null, invitation.GetProperty("invitationToken").ValueKind);
        using var repeated = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        Assert.Equal(ack, await repeated.Content.ReadAsStringAsync(ct));
        using var normalized = await Mutate(owner, HttpMethod.Post, route, new { email = " INVITE-RETRY@example.test ", surface = "internal", targetRole = "admin" }, key);
        Assert.Equal(HttpStatusCode.Created, normalized.StatusCode); Assert.Equal(ack, await normalized.Content.ReadAsStringAsync(ct));
        using var reused = await Mutate(owner, HttpMethod.Post, route, new { email = "another@example.test", surface = "INTERNAL", targetRole = "ADMIN" }, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        Assert.True((await app.Services.GetRequiredService<IInvitationService>().RevokeAsync(org, actor, invitation.GetProperty("id").GetGuid(), "fixture", ct)).Succeeded);
        using var revokedRetry = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, revokedRetry.StatusCode);
        Assert.Equal(ack, await revokedRetry.Content.ReadAsStringAsync(ct));
        var receipt = await app.Services.GetRequiredService<IInvitationStore>().FindCreationReplayAsync(org, actor, Guid.Parse(key), ct);
        Assert.NotNull(receipt!.Invitation.RevokedAt);
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task PRD60_invitation_creation_rejects_invalid_retry_keys(string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invalid invitation key" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var response = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", new { email = "invite@example.test", surface = "INTERNAL", targetRole = "MEMBER" }, key);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PRD60_invitation_creation_replay_requires_current_administration_and_owner_grant_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Fresh invitation authority" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>(); var key = Guid.NewGuid().ToString();
        var input = new { email = "owner-grant@example.test", surface = "INTERNAL", targetRole = "OWNER" }; var route = $"/organizations/{org}/invitations";
        using var first = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        using var demoted = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Forbidden, demoted.StatusCode);
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var denied = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("owner-grant@example.test", await denied.Content.ReadAsStringAsync(ct));
    }
}
