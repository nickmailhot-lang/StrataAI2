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
    public async Task PRD_03_concurrent_removal_replay_preserves_rejoined_membership_and_requires_current_administration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var target = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Removal receipt" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var receipts = app.Services.GetRequiredService<IOrganizationRemovalReplayStore>();
        await store.AddOrRestoreMemberAsync(org, target, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var original = (await store.FindMembershipAsync(org, target, ct))!;
        var key = Guid.NewGuid(); var path = $"/organizations/{org}/members/{target}?expectedVersion={original.Version}";
        using var accountMismatch = await Mutate(owner, HttpMethod.Delete, $"{path}&expectedActorId={target}", new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, accountMismatch.StatusCode);
        Assert.Equal(original, await store.FindMembershipAsync(org, target, ct));
        Assert.Null(await receipts.ReadAsync(org, actor, key, ct));
        var replies = await Task.WhenAll(Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString()),
            Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString()));
        try { Assert.All(replies, reply => Assert.Equal(HttpStatusCode.NoContent, reply.StatusCode)); }
        finally { foreach (var reply in replies) reply.Dispose(); }
        Assert.False((await store.FindMembershipAsync(org, target, ct))!.Active);
        var receipt = await receipts.ReadAsync(org, actor, key, ct); Assert.NotNull(receipt);
        await store.AddOrRestoreMemberAsync(org, target, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var rejoined = await store.FindMembershipAsync(org, target, ct);
        using var replay = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(rejoined, await store.FindMembershipAsync(org, target, ct));
        Assert.Equal(receipt, await receipts.ReadAsync(org, actor, key, ct));
        using var changedVersion = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{target}?expectedVersion={rejoined!.Version}", new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Conflict, changedVersion.StatusCode);
        using var changedTarget = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{actor}?expectedVersion=1", new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Conflict, changedTarget.StatusCode);
        Assert.Equal(rejoined, await store.FindMembershipAsync(org, target, ct));
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var demoted = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.Equal(HttpStatusCode.NotFound, demoted.StatusCode);
        Assert.Equal(rejoined, await store.FindMembershipAsync(org, target, ct));
    }

    [Fact]
    public async Task PRD_03_self_removal_can_acknowledge_original_command_after_membership_retirement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var second = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(second);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var successor = (await second.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Self removal receipt" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        await store.AddOrRestoreMemberAsync(org, successor, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        var key = Guid.NewGuid().ToString(); var path = $"/organizations/{org}/members/{actor}?expectedVersion=1";
        using var removed = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.False((await store.FindMembershipAsync(org, actor, ct))!.Active);
        using var logout = await Mutate(owner, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }
}
