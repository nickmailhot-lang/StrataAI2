using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_HTTP_review_transition_conflict_and_original_recovery()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "HTTP lifecycle parent", ct);
        var route = $"/organizations/{parent.Id}/operational-lifecycle";
        using var initial = await owner.GetAsync(route, ct); Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.True(initial.Headers.CacheControl!.Private); Assert.True(initial.Headers.CacheControl.NoStore);
        var review = await initial.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("ACTIVE", review.GetProperty("status").GetString()); Assert.Equal(actor, review.GetProperty("actorId").GetGuid());
        var revision = review.GetProperty("version").GetInt64(); var key = Guid.NewGuid().ToString("D");
        using var suspended = await Mutate(owner, HttpMethod.Patch, route, new { status = "SUSPENDED", version = revision }, key);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode); var original = await suspended.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("SUSPENDED", original.GetProperty("status").GetString()); Assert.Equal(revision+1, original.GetProperty("version").GetInt64());
        using var inactive = await owner.GetAsync(route, ct); Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        Assert.Equal("SUSPENDED", (await inactive.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("status").GetString());
        using var operational = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = parent.Id, name = "Blocked Board" });
        Assert.Equal(HttpStatusCode.NotFound, operational.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Patch, route, new { status = "DEACTIVATED", version = revision }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var changedKey = await Mutate(owner, HttpMethod.Patch, route, new { status = "ACTIVE", version = revision }, key);
        Assert.Equal(HttpStatusCode.Conflict, changedKey.StatusCode);
        using var reactivated = await Mutate(owner, HttpMethod.Patch, route, new { status = "ACTIVE", version = revision+1 }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        using var retried = await Mutate(owner, HttpMethod.Patch, route, new { status = "SUSPENDED", version = revision }, key);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(original.GetRawText(), (await retried.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        var current = await owner.GetFromJsonAsync<JsonElement>(route, ct);
        Assert.Equal("ACTIVE", current.GetProperty("status").GetString()); Assert.Equal(revision+2, current.GetProperty("version").GetInt64());
        using var deactivated = await Mutate(owner, HttpMethod.Patch, route, new { status = "DEACTIVATED", version = revision+2 }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var finalReview = await owner.GetAsync(route, ct); Assert.Equal(HttpStatusCode.OK, finalReview.StatusCode);
        Assert.Equal("DEACTIVATED", (await finalReview.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_HTTP_target_admission_precedes_invalid_body_and_key()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var stranger = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(stranger);
        var (_, parent) = await ConfigurationParent(owner, "Private lifecycle title", ct);
        var route = $"/organizations/{parent.Id}/operational-lifecycle";
        using var deniedReview = await stranger.GetAsync(route, ct); Assert.Equal(HttpStatusCode.NotFound, deniedReview.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Patch, route) { Content = new StringContent("private malformed content", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-StrataAI-Request", "1");
        using var deniedChange = await stranger.SendAsync(request, ct); Assert.Equal(HttpStatusCode.NotFound, deniedChange.StatusCode);
        var denial = await deniedChange.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("Private lifecycle title", denial); Assert.DoesNotContain("private malformed", denial);
        using var wrongActor = new HttpRequestMessage(HttpMethod.Get, route);
        wrongActor.Headers.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString("D"));
        using var actorDenial = await owner.SendAsync(wrongActor, ct); Assert.Equal(HttpStatusCode.Unauthorized, actorDenial.StatusCode);
        using var csrf = await owner.PatchAsJsonAsync(route, new { status = "SUSPENDED", version = parent.Version }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
    }

    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_HTTP_strict_bounded_request_validation_preserves_state()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Validated lifecycle parent", ct);
        var route = $"/organizations/{parent.Id}/operational-lifecycle";
        foreach (var json in new[] { "null", "{}", "{\"status\":\"SUSPENDED\"}",
            "{\"version\":1,\"status\":\"ACTIVE\",\"status\":\"SUSPENDED\"}",
            "{\"version\":1,\"status\":\"SUSPENDED\",\"extra\":true}",
            "{\"version\":1,\"status\":4}", "{\"version\":1,\"status\":\"DELETED\"}",
            "{\"version\":1,\"status\":\"suspended\"}", "{\"version\":1,\"status\":null}", new string(' ',4097) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, route) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-StrataAI-Request", "1"); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            using var response = await owner.SendAsync(request, ct); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var missingKey = await Mutate(owner, HttpMethod.Patch, route, new { status = "SUSPENDED", version = parent.Version });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        var review = await owner.GetFromJsonAsync<JsonElement>(route, ct);
        Assert.Equal("ACTIVE", review.GetProperty("status").GetString()); Assert.Equal(parent.Version, review.GetProperty("version").GetInt64());
    }
}
