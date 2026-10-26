using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private static object UnitRequest(long version, string unit = "48", string status = "ACTIVE") => new { version,
        data = new { unitIdentifier = unit, lotIdentifier = "SL48", civicAddress = (string?)null,
            building = (string?)null, floor = (string?)null, type = (string?)null, status } };

    [Fact]
    public async Task PRD_28_HTTP_create_refresh_correct_deactivate_history_and_original_receipt() {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Unit directory", ct);
        var root = $"/organizations/{parent.Id}/strata-lots"; var path = root + "/" + Guid.NewGuid();
        using var empty = await owner.GetAsync(root, ct); Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty((await empty.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray());
        var key = Guid.NewGuid().ToString("D");
        using var created = await Mutate(owner, HttpMethod.Patch, path, UnitRequest(0), key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.True(created.Headers.CacheControl!.Private); Assert.True(created.Headers.CacheControl.NoStore);
        var original = await created.Content.ReadAsStringAsync(ct);
        using var fresh = await owner.GetAsync(path, ct); Assert.Equal(original, await fresh.Content.ReadAsStringAsync(ct));
        using var corrected = await Mutate(owner, HttpMethod.Patch, path, UnitRequest(1, "48B"), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        using var deactivated = await Mutate(owner, HttpMethod.Patch, path, UnitRequest(2, "48B", "INACTIVE"), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var retry = await Mutate(owner, HttpMethod.Patch, path, UnitRequest(0), key);
        Assert.Equal(original, await retry.Content.ReadAsStringAsync(ct));
        using var changedIntent = await Mutate(owner, HttpMethod.Patch, path, UnitRequest(0, "49"), key);
        Assert.Equal(HttpStatusCode.Conflict, changedIntent.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Patch, path, UnitRequest(1), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var history = await owner.GetAsync(path + "/history", ct);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var rows = (await history.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new long[] { 3, 2, 1 }, rows.Select(x => x.GetProperty("lot").GetProperty("version").GetInt64()));
        Assert.Equal("48", rows[2].GetProperty("lot").GetProperty("data").GetProperty("unitIdentifier").GetString());
        Assert.DoesNotContain("fingerprint", await history.Content.ReadAsStringAsync(ct));
    }
    [Fact]
    public async Task PRD_28_HTTP_foreign_anonymous_switched_actor_and_invalid_cursor_do_not_disclose_units() {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(foreign);
        var (_, parent) = await ConfigurationParent(owner, "Private units", ct);
        var root = $"/organizations/{parent.Id}/strata-lots";
        using var denied = await foreign.GetAsync(root + "?after=invalid", ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var badWrite = await Mutate(foreign, HttpMethod.Patch, root + "/" + Guid.NewGuid(), new { invalid = true }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.NotFound, badWrite.StatusCode);
        using var noSession = await anonymous.GetAsync(root, ct); Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        using var malformed = await owner.GetAsync(root + "?after=invalid", ct); Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        owner.DefaultRequestHeaders.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString("D"));
        using var switched = await owner.GetAsync(root, ct); Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
    }
    [Fact]
    public async Task PRD_28_HTTP_duplicate_identifier_is_atomic_and_tenants_reserve_independently() {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Unique units", ct);
        var root = $"/organizations/{parent.Id}/strata-lots";
        using var first = await Mutate(owner, HttpMethod.Patch, root + "/" + Guid.NewGuid(), UnitRequest(0), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var duplicatePath = root + "/" + Guid.NewGuid();
        using var second = await Mutate(owner, HttpMethod.Patch, duplicatePath, UnitRequest(0), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var absent = await owner.GetAsync(duplicatePath, ct); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var (_, otherParent) = await ConfigurationParent(owner, "Other units", ct);
        using var other = await Mutate(owner, HttpMethod.Patch, $"/organizations/{otherParent.Id}/strata-lots/{Guid.NewGuid()}", UnitRequest(0), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
    [Fact]
    public async Task PRD_28_HTTP_directory_pages_all_55_units_without_duplicates_or_missing_rows() {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "55-unit directory", ct);
        var root = $"/organizations/{parent.Id}/strata-lots";
        for (var n = 1; n <= 55; n++) {
            using var saved = await Mutate(owner, HttpMethod.Patch, root + "/" + Guid.NewGuid(), new { version = 0,
                data = new { unitIdentifier = n.ToString(System.Globalization.CultureInfo.InvariantCulture), lotIdentifier = "SL" + n,
                    civicAddress = (string?)null, building = (string?)null, floor = (string?)null, type = (string?)null, status = "ACTIVE" } },
                Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }
        var first = await owner.GetFromJsonAsync<JsonElement>(root, ct);
        var firstIds = first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(50, firstIds.Length);
        var after = first.GetProperty("nextCursor").GetGuid(); Assert.Equal(firstIds[^1], after);
        var second = await owner.GetFromJsonAsync<JsonElement>(root + "?after=" + after, ct);
        var remaining = second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(5, remaining.Length); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(55, firstIds.Concat(remaining).Distinct().Count());
    }
    [Theory]
    [InlineData("{\"version\":0,\"data\":null}")]
    [InlineData("{\"version\":0,\"version\":0,\"data\":{}}")]
    [InlineData("{\"version\":0,\"data\":{\"unitIdentifier\":\"48\"}}")]
    [InlineData("{\"version\":0,\"data\":{},\"owner\":\"private\"}")]
    public async Task PRD_28_HTTP_rejects_missing_unknown_and_duplicate_fields(string json) {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Strict unit input", ct);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/organizations/{parent.Id}/strata-lots/{Guid.NewGuid()}");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D")); request.Headers.Add("X-StrataAI-Request", "1");
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await owner.SendAsync(request, ct); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
