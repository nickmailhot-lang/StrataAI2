using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private static object PolicyRequest(long version, int? days = 365) => new { version,
        policy = new { recordClass = "document", category = "contracts", jurisdiction = "CA-BC", anchor = "closed",
            retentionDays = days, action = "preserve", source = "Private policy source", notes = "Private notes" } };

    [Fact]
    public async Task PRD_70_policy_HTTP_persists_refresh_history_original_retry_and_private_cache_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "HTTP policy", ct);
        var root = $"/organizations/{parent.Id}/retention-policies"; var id = Guid.NewGuid(); var path = root + "/" + id;
        using var empty = await owner.GetAsync(root, ct); Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty((await empty.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray());
        var key = Guid.NewGuid().ToString("D");
        using var first = await Mutate(owner, HttpMethod.Patch, path, PolicyRequest(0), key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl!.Private); Assert.True(first.Headers.CacheControl.NoStore);
        var body = await first.Content.ReadAsStringAsync(ct);
        using var fresh = await owner.GetAsync(path, ct); Assert.Equal(body, await fresh.Content.ReadAsStringAsync(ct));
        using var second = await Mutate(owner, HttpMethod.Patch, path, PolicyRequest(1, 730), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var retry = await Mutate(owner, HttpMethod.Patch, path, PolicyRequest(0), key);
        Assert.Equal(body, await retry.Content.ReadAsStringAsync(ct));
        using var changed = await Mutate(owner, HttpMethod.Patch, path, PolicyRequest(0, 1), key); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Patch, path, PolicyRequest(0), Guid.NewGuid().ToString("D")); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var history = await owner.GetAsync(path + "/history", ct); Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var items = (await history.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new long[] { 2, 1 }, items.Select(x => x.GetProperty("policy").GetProperty("version").GetInt64()));
        Assert.DoesNotContain("fingerprint", await history.Content.ReadAsStringAsync(ct)); Assert.DoesNotContain("keyId", await history.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task PRD_70_policy_HTTP_denies_foreign_anonymous_and_expected_actor_mismatch_before_private_validation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(foreign);
        var (_, parent) = await ConfigurationParent(owner, "HTTP policy scope", ct);
        var path = $"/organizations/{parent.Id}/retention-policies/{Guid.NewGuid()}";
        using var denied = await Mutate(foreign, HttpMethod.Patch, path, new { invalid = true }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.DoesNotContain("policy", await denied.Content.ReadAsStringAsync(ct));
        using var invalidCursor = await foreign.GetAsync(path + "/history?beforeVersion=bad", ct); Assert.Equal(HttpStatusCode.NotFound, invalidCursor.StatusCode);
        using var noSession = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        owner.DefaultRequestHeaders.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString("D"));
        using var switched = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
    }

    [Theory]
    [InlineData("{\"version\":0,\"policy\":null}")]
    [InlineData("{\"version\":0,\"version\":0,\"policy\":{}}")]
    [InlineData("{\"version\":0,\"policy\":{\"recordClass\":0}}")]
    [InlineData("{\"version\":0,\"policy\":{\"recordClass\":\"document\",\"category\":\"contracts\",\"anchor\":\"created\",\"retentionDays\":1,\"action\":\"delete\",\"source\":\"Explicit\",\"unsupported\":true}}")]
    public async Task PRD_70_policy_HTTP_strictly_rejects_incomplete_duplicate_numeric_enum_and_unknown_fields(string json)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Strict policy input", ct);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/organizations/{parent.Id}/retention-policies/{Guid.NewGuid()}");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        // Reuse the app's same-origin mutation admission.
        request.Headers.Add("X-StrataAI-Request", "1");
        using var response = await owner.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
