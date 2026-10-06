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
    public async Task PRD_03_directory_bounds_routes_and_rechecks_membership_before_disclosure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var member = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(member); await RegisterAndLogin(outsider);
        var actor = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var other = (await outsider.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var ids = Enumerable.Range(1, 52).Select(n => Guid.Parse($"90000000-0000-4000-8000-{n:000000000000}")).ToArray();
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids) await store.CreateOrganizationAsync(actor, id, $"Private directory {id}", null, now, ct);
        // Keep a removed routing hint without violating the sole-owner floor.
        await store.AddOrRestoreMemberAsync(ids[0], other, OrganizationRole.Owner, now, ct);
        Assert.Equal(OrganizationRemoveMemberResult.Removed, await store.RemoveMemberAsync(ids[0], actor, now, ct));
        Assert.True(await store.MarkDeletingAsync(ids[1], 1, now, ct));
        using var first = await member.GetAsync("/organizations/directory", ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl!.Private); Assert.True(first.Headers.CacheControl.NoStore);
        var page = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var firstIds = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("organization").GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(48, firstIds.Length); Assert.DoesNotContain(ids[0], firstIds); Assert.DoesNotContain(ids[1], firstIds);
        Assert.Equal(ids[49], page.GetProperty("nextCursor").GetGuid());
        var tail = await member.GetFromJsonAsync<JsonElement>($"/organizations/directory?after={ids[49]}", ct);
        var tailIds = tail.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("organization").GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(ids.Skip(2).ToArray(), firstIds.Concat(tailIds).ToArray());
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);
        var foreignPage = await outsider.GetFromJsonAsync<JsonElement>("/organizations/directory", ct);
        Assert.Single(foreignPage.GetProperty("items").EnumerateArray());
        Assert.Equal(ids[0], foreignPage.GetProperty("items")[0].GetProperty("organization").GetProperty("id").GetGuid());
        foreach (var cursor in new[] { "bad", Guid.Empty.ToString("D"), ids[0].ToString("N") })
        {
            using var invalid = await member.GetAsync($"/organizations/directory?after={cursor}", ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_organization_cursor", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
        // PRD-03-TC-02/05: a fully omitted page still advances to later active grants.
        var removedHints = Enumerable.Range(1, 50).Select(n => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}")).ToArray();
        foreach (var id in removedHints)
        {
            await store.CreateOrganizationAsync(other, id, "Revoked private Organization", null, now, ct);
            await store.AddOrRestoreMemberAsync(id, actor, OrganizationRole.Member, now, ct);
            Assert.Equal(OrganizationRemoveMemberResult.Removed, await store.RemoveMemberAsync(id, actor, now, ct));
        }
        using var empty = await member.GetAsync("/organizations/directory", ct);
        var emptyBody = await empty.Content.ReadAsStringAsync(ct);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyPage = JsonDocument.Parse(emptyBody);
        Assert.Empty(emptyPage.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(removedHints[^1], emptyPage.RootElement.GetProperty("nextCursor").GetGuid());
        Assert.DoesNotContain("Revoked private Organization", emptyBody);
        var continued = await member.GetFromJsonAsync<JsonElement>($"/organizations/directory?after={removedHints[^1]}", ct);
        Assert.Equal(firstIds, continued.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("organization").GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(ids[49], continued.GetProperty("nextCursor").GetGuid());
        using var denied = await anonymous.GetAsync("/organizations/directory", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
}
