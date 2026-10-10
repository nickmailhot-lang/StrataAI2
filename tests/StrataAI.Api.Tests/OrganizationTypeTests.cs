using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-27-FR-002 / TC-01/05/07/08: actual creation, refresh and intent identity.
    [Theory]
    [InlineData(null, "STRATA")]
    [InlineData("STRATA", "STRATA")]
    [InlineData("HOA", "HOA")]
    [InlineData("CONDOMINIUM", "CONDOMINIUM")]
    [InlineData("COOPERATIVE", "COOPERATIVE")]
    [InlineData("PROPERTY_MANAGEMENT_COMPANY", "PROPERTY_MANAGEMENT_COMPANY")]
    [InlineData("GENERIC", "GENERIC")]
    public async Task PRD_27_Organization_type_is_persisted_and_preserved_by_metadata_and_receipt_replay(string? requested, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var body = new Dictionary<string, object?> { ["name"] = "Typed organization", ["description"] = "Original" };
        if (requested is not null) body["type"] = requested;
        var key = Guid.NewGuid().ToString();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.Content.ReadAsStringAsync(ct);
        var organization = JsonDocument.Parse(original).RootElement.GetProperty("organization");
        Assert.Equal(expected, organization.GetProperty("type").GetString());
        var id = organization.GetProperty("id").GetGuid();
        using var denied = await other.GetAsync($"/organizations/{id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Typed organization", await denied.Content.ReadAsStringAsync(ct));
        using var edited = await Mutate(owner, HttpMethod.Patch, $"/organizations/{id}", new { name = "Renamed organization", version = 1 });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(expected, (await edited.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("type").GetString());
        var current = await store.FindOrganizationAsync(id, ct);
        using var replay = await Mutate(owner, HttpMethod.Post, "/organizations", body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync(ct));
        var changed = new Dictionary<string, object?>(body) { ["type"] = expected == "HOA" ? "STRATA" : "HOA" };
        using var conflict = await Mutate(owner, HttpMethod.Post, "/organizations", changed, key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency_conflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(current, await store.FindOrganizationAsync(id, ct));
        Assert.Single(await store.ListOrganizationsForUserAsync(actor, ct));
        var refreshed = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{id}", ct);
        Assert.Equal(expected, refreshed.GetProperty("organization").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("strata")]
    [InlineData("UNKNOWN")]
    [InlineData("1")]
    public async Task PRD_27_Invalid_Organization_type_is_refused_without_creating_records(string type)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var refused = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Refused type", type }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("invalid_organization_type", (await refused.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Empty(await app.Services.GetRequiredService<IOrganizationStore>().ListOrganizationsForUserAsync(actor, ct));
    }
}
