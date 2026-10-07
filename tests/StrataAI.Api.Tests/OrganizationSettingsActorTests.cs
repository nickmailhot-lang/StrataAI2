using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-03-TC-05/07: a valid replacement administrator cookie must not
    // transfer a reviewed edit or disclose another actor's original receipt.
    [Fact]
    public async Task Organization_settings_actor_guard_rejects_replacement_malformed_and_multiple_identity_before_read_or_write()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var admin = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(admin);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Private reviewed organization" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var receipts = app.Services.GetRequiredService<IOrganizationMetadataReplayStore>();
        await store.AddOrRestoreMemberAsync(org, adminId, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var before = await store.FindOrganizationAsync(org, ct); var key = Guid.NewGuid();
        var body = new { name = "Reviewed edit", version = before!.Version };
        foreach (var expected in new[] { new[] { ownerId.ToString() }, new[] { "invalid" }, new[] { Guid.Empty.ToString() }, new[] { adminId.ToString(), adminId.ToString() } })
        {
            admin.DefaultRequestHeaders.Remove("X-StrataAI-Expected-Actor");
            admin.DefaultRequestHeaders.Add("X-StrataAI-Expected-Actor", expected);
            using var read = await admin.GetAsync($"/organizations/{org}", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
            Assert.True(read.Headers.CacheControl!.NoStore); Assert.True(read.Headers.CacheControl.Private);
            Assert.Equal("session_unavailable", (await read.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            Assert.DoesNotContain("Private reviewed organization", await read.Content.ReadAsStringAsync(ct));
            using var denied = await Mutate(admin, HttpMethod.Patch, $"/organizations/{org}", body, key.ToString());
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal("session_unavailable", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            Assert.DoesNotContain("Reviewed edit", await denied.Content.ReadAsStringAsync(ct));
            Assert.Equal(before, await store.FindOrganizationAsync(org, ct));
            Assert.Null(await receipts.ReadAsync(org, adminId, key, ct));
        }
        admin.DefaultRequestHeaders.Remove("X-StrataAI-Expected-Actor");
        admin.DefaultRequestHeaders.Add("X-StrataAI-Expected-Actor", adminId.ToString());
        using var accepted = await Mutate(admin, HttpMethod.Patch, $"/organizations/{org}", body, key.ToString());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(before.Version + 1, (await store.FindOrganizationAsync(org, ct))!.Version);
        admin.DefaultRequestHeaders.Remove("X-StrataAI-Expected-Actor");
        admin.DefaultRequestHeaders.Add("X-StrataAI-Expected-Actor", ownerId.ToString());
        using var hidden = await Mutate(admin, HttpMethod.Patch, $"/organizations/{org}", body, key.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, hidden.StatusCode);
        Assert.DoesNotContain("Reviewed edit", await hidden.Content.ReadAsStringAsync(ct));
        admin.DefaultRequestHeaders.Remove("X-StrataAI-Expected-Actor");
        admin.DefaultRequestHeaders.Add("X-StrataAI-Expected-Actor", adminId.ToString());
        using var replay = await Mutate(admin, HttpMethod.Patch, $"/organizations/{org}", body, key.ToString());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await accepted.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(before.Version + 1, (await store.FindOrganizationAsync(org, ct))!.Version);
    }
}
