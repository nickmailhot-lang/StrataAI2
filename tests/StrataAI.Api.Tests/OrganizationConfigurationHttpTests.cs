using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_configuration_HTTP_persists_refresh_history_and_original_retry_after_later_change()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Configuration HTTP parent", ct);
        var path = $"/organizations/{parent.Id}/configuration";
        using var empty = await owner.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.True(empty.Headers.CacheControl!.Private); Assert.True(empty.Headers.CacheControl.NoStore);
        var view = await empty.Content.ReadFromJsonAsync<OrganizationConfigurationView>(ct);
        Assert.Equal(0, view!.Version); Assert.Null(view.Revision);
        var key = Guid.NewGuid().ToString("D");
        var data = new OrganizationConfigurationData("Reviewed legal name", "CA-BC", "America/Vancouver",
            CorporationIdentifier: "HTTP-EXAMPLE", ManagementCompanyName: "Original management",
            JurisdictionPolicies: [new("review_cycle", "12", "Administrator supplied policy", "Review annually")]);
        using var first = await Mutate(owner, HttpMethod.Patch, path, new { version = 0, configuration = data }, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var original = await first.Content.ReadAsStringAsync(ct);
        var revision = await first.Content.ReadFromJsonAsync<OrganizationConfigurationRecord>(ct);
        Assert.Equal(1, revision!.Version); Assert.Equal(data.LegalName, revision.Configuration.LegalName);
        using var second = await Mutate(owner, HttpMethod.Patch, path, new { version = 1, configuration = data with
        { Jurisdiction = "CA-ON", Timezone = "America/Toronto", ManagementCompanyName = "Later management" } }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var retry = await Mutate(owner, HttpMethod.Patch, path, new { version = 0, configuration = data }, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(original, await retry.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Patch, path, new { version = 0, configuration = data }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        var current = await owner.GetFromJsonAsync<OrganizationConfigurationView>(path, ct);
        Assert.Equal(2, current!.Version); Assert.Equal("Later management", current.Revision!.Configuration.ManagementCompanyName);
        var history = await owner.GetFromJsonAsync<OrganizationConfigurationHistoryPage>(path + "/history", ct);
        Assert.Equal(new long[] { 2, 1 }, history!.Items.Select(item => item.Version));
        Assert.Equal("Original management", history.Items[1].Configuration.ManagementCompanyName);
        var older = await owner.GetFromJsonAsync<OrganizationConfigurationHistoryPage>(path + "/history?beforeVersion=2", ct);
        Assert.Single(older!.Items); Assert.Equal(1, older.Items[0].Version); Assert.Null(older.NextBeforeVersion);
    }

    [Theory]
    [InlineData("{\"configuration\":{\"legalName\":\"Reviewed\",\"jurisdiction\":\"CA-BC\",\"timezone\":\"UTC\"}}")]
    [InlineData("{\"version\":0}")]
    [InlineData("{\"version\":0,\"version\":1,\"configuration\":null}")]
    [InlineData("{\"version\":0,\"configuration\":{\"legalName\":\"First\",\"legalName\":\"Second\",\"jurisdiction\":\"CA-BC\",\"timezone\":\"UTC\"}}")]
    [InlineData("{\"version\":0,\"configuration\":null,\"unexpected\":true}")]
    [InlineData("{\"version\":0,\"configuration\":{\"legalName\":\"Reviewed\",\"jurisdiction\":\"CA-BC\",\"timezone\":\"UTC\",\"unexpected\":true}}")]
    [InlineData("{\"version\":0,\"configuration\":{\"legalName\":\"Reviewed\",\"jurisdiction\":\"CA-BC\",\"timezone\":\"UTC\",\"jurisdictionPolicies\":[{\"key\":\"sample\",\"value\":\"12\",\"source\":\"Reviewed\",\"unexpected\":true}]}}")]
    public async Task PRD_27_configuration_HTTP_rejects_unknown_and_missing_schema_fields_without_revisions(string body)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Schema parent", ct);
        var path = $"/organizations/{parent.Id}/configuration";
        using var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-StrataAI-Request", "1");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await owner.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_configuration_request", (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(0, (await owner.GetFromJsonAsync<OrganizationConfigurationView>(path, ct))!.Version);
        Assert.Empty((await owner.GetFromJsonAsync<OrganizationConfigurationHistoryPage>(path + "/history", ct))!.Items);
    }

    [Fact]
    public async Task PRD_27_configuration_HTTP_denies_substituted_actor_and_foreign_target_before_private_input_validation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var (_, parent) = await ConfigurationParent(owner, "Private HTTP parent", ct);
        var (otherActor, _) = await ConfigurationParent(outsider, "Other HTTP parent", ct);
        var path = $"/organizations/{parent.Id}/configuration";
        using var saved = await Mutate(owner, HttpMethod.Patch, path, new { version = 0,
            configuration = new OrganizationConfigurationData("Protected HTTP legal name", "CA-BC", "UTC") }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        foreach (var uri in new[] { path, path + "/history?beforeVersion=invalid" })
        {
            using var denied = await outsider.GetAsync(uri, ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.DoesNotContain("Protected HTTP legal name", await denied.Content.ReadAsStringAsync(ct));
        }
        using var malformed = new HttpRequestMessage(HttpMethod.Patch, path)
        { Content = new StringContent("{invalid", Encoding.UTF8, "application/json") };
        malformed.Headers.Add("X-StrataAI-Request", "1");
        using var refused = await outsider.SendAsync(malformed, ct);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal("organization_not_found", (await refused.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var swapped = new HttpRequestMessage(HttpMethod.Get, path);
        swapped.Headers.Add("X-StrataAI-Expected-Actor", otherActor.ToString("D"));
        using var mismatch = await owner.SendAsync(swapped, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, mismatch.StatusCode);
        Assert.DoesNotContain("Protected HTTP legal name", await mismatch.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task PRD_27_configuration_HTTP_requires_retry_key_and_returns_safe_field_errors_without_effects()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Validation HTTP parent", ct);
        var path = $"/organizations/{parent.Id}/configuration";
        var data = new OrganizationConfigurationData("Reviewed legal name", "CA-BC", "UTC");
        using var missing = await Mutate(owner, HttpMethod.Patch, path, new { version = 0, configuration = data });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("idempotency_key_required", (await missing.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var invalid = await Mutate(owner, HttpMethod.Patch, path, new { version = 0,
            configuration = data with { Timezone = "private-invalid-timezone" } }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var failure = await invalid.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("private-invalid-timezone", failure); Assert.Contains("invalid_configuration_Timezone", failure);
        using var oversized = new HttpRequestMessage(HttpMethod.Patch, path)
        { Content = new StringContent(new string(' ', 98_305), Encoding.UTF8, "application/json") };
        oversized.Headers.Add("X-StrataAI-Request", "1");
        oversized.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var bounded = await owner.SendAsync(oversized, ct); Assert.Equal(HttpStatusCode.BadRequest, bounded.StatusCode);
        Assert.Equal("invalid_configuration_request", (await bounded.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        var current = await owner.GetFromJsonAsync<OrganizationConfigurationView>(path, ct);
        Assert.Equal(0, current!.Version); Assert.Null(current.Revision);
        Assert.Empty((await owner.GetFromJsonAsync<OrganizationConfigurationHistoryPage>(path + "/history", ct))!.Items);
    }
}
