using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-03-TC-08 / WS-FR-005/006: the last active owner cannot depart.
    [Fact]
    public async Task Concurrent_owner_departures_preserve_one_active_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var first = app.CreateClient(); using var second = app.CreateClient();
        await RegisterAndLogin(first); await RegisterAndLogin(second);
        var secondId = (await second.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(first, HttpMethod.Post, "/organizations", new { name = "Owner floor" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        await store.AddOrRestoreMemberAsync(org, secondId, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        var departures = await Task.WhenAll(Mutate(first, HttpMethod.Post, $"/organizations/{org}/leave", new { }),
            Mutate(second, HttpMethod.Post, $"/organizations/{org}/leave", new { }));
        try
        {
            Assert.Single(departures, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(departures, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in departures) response.Dispose(); }
        var firstId = (await first.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var memberships = new[] { await store.FindMembershipAsync(org, firstId, ct), await store.FindMembershipAsync(org, secondId, ct) };
        Assert.Single(memberships, member => member is { Active: true, Role: OrganizationRole.Owner });
    }

    // PRD-03-TC-10 / PRD-18: deletion freezes subsequent organization mutations.
    [Fact]
    public async Task Deleting_organization_rejects_metadata_changes_and_departure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Deletion freeze" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var deleting = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}?version=1", new { });
        Assert.Equal(HttpStatusCode.Accepted, deleting.StatusCode);
        using var update = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", new { name = "Denied", version = 2 });
        using var leave = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/leave", new { });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, leave.StatusCode);
        Assert.Equal("Deletion freeze", (await app.Services.GetRequiredService<IOrganizationStore>().FindOrganizationAsync(org, ct))?.Name);
    }
}
