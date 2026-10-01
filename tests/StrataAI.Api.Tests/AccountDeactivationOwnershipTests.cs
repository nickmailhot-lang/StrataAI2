using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02/03-TC-05/07: refusal preserves the account, sessions, events and retry key.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_deactivation_refuses_sole_owner_then_same_intent_succeeds_after_another_owner_is_added(bool keyed)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        var cookie = await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var user = await owner.GetFromJsonAsync<JsonElement>("/me", ct);
        var id = user.GetProperty("id").GetGuid();
        var otherId = (await other.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Continuity" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var before = await identities.FindUserByIdAsync(id, ct);
        var events = (await identities.ReadEventsAsync(id, 0, ct)).Value!.Events.Count;
        var key = Guid.NewGuid();
        using var rejected = await Mutate(owner, HttpMethod.Post, "/me/deactivate", new { }, keyed ? key.ToString() : null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("organization_owner_required", (await rejected.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.DoesNotContain(org.ToString(), await rejected.Content.ReadAsStringAsync(ct));
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, await identities.FindUserByIdAsync(id, ct));
        Assert.Equal(events, (await identities.ReadEventsAsync(id, 0, ct)).Value!.Events.Count);
        Assert.Null(await app.Services.GetRequiredService<IIdentityRevocationReplayStore>().ReadAsync(id, key, ct));
        using var stillAuthenticated = await owner.GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.OK, stillAuthenticated.StatusCode);
        await organizations.AddOrRestoreMemberAsync(org, otherId, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        using var copied = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        copied.DefaultRequestHeaders.Add("Cookie", cookie);
        using var success = await Mutate(copied, HttpMethod.Post, "/me/deactivate", new { }, keyed ? key.ToString() : null);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Equal(AccountStatus.Deactivated, (await identities.FindUserByIdAsync(id, ct))!.Status);
        Assert.True((await organizations.FindMembershipAsync(org, id, ct))!.Active);
        Assert.Equal(events + 1, (await identities.ReadEventsAsync(id, 0, ct)).Value!.Events.Count);
        if (keyed)
        {
            // A historical acknowledgment must not repeat the floor or mutation.
            Assert.True(await identities.DeactivateUserAsync(otherId, DateTimeOffset.UtcNow, ct));
            using var replay = await Mutate(copied, HttpMethod.Post, "/me/deactivate", new { }, key.ToString());
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.Equal(events + 1, (await identities.ReadEventsAsync(id, 0, ct)).Value!.Events.Count);
            using var protectedRead = await copied.GetAsync("/organizations", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, protectedRead.StatusCode);
        }
    }

    // PRD-02/03-TC-08: one global account decision covers every owned Organization.
    [Fact]
    public async Task Account_deactivation_requires_continuity_in_every_owned_organization_and_does_not_count_an_admin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var id = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var otherId = (await other.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var ids = new List<Guid>();
        foreach (var name in new[] { "First continuity", "Second continuity" })
        {
            using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name });
            ids.Add((await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid());
        }
        await organizations.AddOrRestoreMemberAsync(ids[0], otherId, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        await organizations.AddOrRestoreMemberAsync(ids[1], otherId, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var before = await identities.FindUserByIdAsync(id, ct);
        var key = Guid.NewGuid().ToString();
        using var rejected = await Mutate(owner, HttpMethod.Post, "/me/deactivate", new { }, key);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(before, await identities.FindUserByIdAsync(id, ct));
        await organizations.AddOrRestoreMemberAsync(ids[1], otherId, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        using var confirmed = await Mutate(owner, HttpMethod.Post, "/me/deactivate", new { }, key);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        foreach (var org in ids) Assert.True((await organizations.FindMembershipAsync(org, otherId, ct))!.Active);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("leave")]
    public async Task Account_deactivation_and_concurrent_owner_departure_preserve_an_active_owner(string firstAction)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var first = app.CreateClient(); using var second = app.CreateClient();
        await RegisterAndLogin(first); await RegisterAndLogin(second);
        var secondId = (await second.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(first, HttpMethod.Post, "/organizations", new { name = "Concurrent continuity" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(org, secondId, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        var replies = await Task.WhenAll(Mutate(first, HttpMethod.Post, firstAction == "leave" ? $"/organizations/{org}/leave" : "/me/deactivate", new { }, firstAction == "leave" ? null : Guid.NewGuid().ToString()),
            Mutate(second, HttpMethod.Post, "/me/deactivate", new { }, Guid.NewGuid().ToString()));
        try
        {
            Assert.Single(replies, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(replies, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in replies) response.Dispose(); }
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var active = new List<Guid>();
        foreach (var id in await organizations.ListActiveOwnerUserIdsAsync(org, ct))
            if ((await identities.FindUserByIdAsync(id, ct))!.Status == AccountStatus.Active) active.Add(id);
        Assert.Single(active);
    }

    [Fact]
    public async Task Account_deactivation_and_owner_leave_do_not_count_an_already_deactivated_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var otherId = (await other.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Inactive owner" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, otherId, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        Assert.True(await app.Services.GetRequiredService<IIdentityStore>().DeactivateUserAsync(otherId, DateTimeOffset.UtcNow, ct));
        using var deactivation = await Mutate(owner, HttpMethod.Post, "/me/deactivate", new { }, Guid.NewGuid().ToString());
        using var leave = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/leave", new { });
        Assert.Equal(HttpStatusCode.Conflict, deactivation.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
        using var stillActive = await owner.GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }
}
