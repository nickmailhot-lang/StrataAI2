using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Concurrent_departure_replay_preserves_a_later_rejoined_membership_and_refuses_revoked_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var actor = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Departure receipt" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var receipts = app.Services.GetRequiredService<IOrganizationDepartureReplayStore>();
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var key = Guid.NewGuid();
        var replies = await Task.WhenAll(Mutate(member, HttpMethod.Post, $"/organizations/{org}/leave", new { }, key.ToString()),
            Mutate(member, HttpMethod.Post, $"/organizations/{org}/leave", new { }, key.ToString()));
        try { Assert.All(replies, reply => Assert.Equal(HttpStatusCode.NoContent, reply.StatusCode)); }
        finally { foreach (var reply in replies) reply.Dispose(); }
        Assert.False((await store.FindMembershipAsync(org, actor, ct))!.Active);
        var receipt = await receipts.ReadAsync(org, actor, key, ct); Assert.NotNull(receipt);
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var rejoined = await store.FindMembershipAsync(org, actor, ct);
        using var replay = await Mutate(member, HttpMethod.Post, $"/organizations/{org}/leave", new { }, key.ToString());
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(rejoined, await store.FindMembershipAsync(org, actor, ct));
        Assert.Equal(receipt, await receipts.ReadAsync(org, actor, key, ct));
        using var logout = await Mutate(member, HttpMethod.Post, "/auth/logout", new { }); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await Mutate(member, HttpMethod.Post, $"/organizations/{org}/leave", new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(rejoined, await store.FindMembershipAsync(org, actor, ct));
    }
}
