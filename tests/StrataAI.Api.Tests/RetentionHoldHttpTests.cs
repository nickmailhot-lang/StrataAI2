using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("card", "operationalWork")]
    [InlineData("cardComment", "operationalWork")]
    [InlineData("attachment", "document")]
    public async Task PRD_70_HTTP_confirmed_record_hold_persists_and_recovers_original_receipts(string kind, string recordClass)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct); await RegisterAndLogin(foreign);
        var id = kind == "card" ? f.Card : kind == "cardComment" ? f.Comment : f.Attachment;
        var source = $"/organizations/{f.Organization}/retention-sources/{kind}/{id}";
        using var reviewResponse = await owner.GetAsync(source + "/review", ct);
        var review = await reviewResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var classified = await Mutate(owner, HttpMethod.Post, source + "/classification",
            new { review, category = "http.hold", jurisdiction = (string?)null, confirmed = true }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, classified.StatusCode);
        var path = $"/organizations/{f.Organization}/retention-holds/{Guid.NewGuid()}";
        var key = Guid.NewGuid().ToString();
        var body = new { recordClass, category = "http.hold", recordId = (Guid?)id, reason = "Private human hold reason", review, confirmed = true };
        using var applied = await Mutate(owner, HttpMethod.Post, path + "/apply", body, key);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.True(applied.Headers.CacheControl!.Private); Assert.True(applied.Headers.CacheControl.NoStore);
        var original = await applied.Content.ReadAsStringAsync(ct);
        using var persisted = await owner.GetAsync(path, ct); Assert.Equal(original, await persisted.Content.ReadAsStringAsync(ct));
        using var stranger = await foreign.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.DoesNotContain("Private human hold reason", await stranger.Content.ReadAsStringAsync(ct));
        using var changed = await Mutate(owner, HttpMethod.Post, path + "/apply", body with { reason = "Changed reason" }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Post, path + "/release", new { version = 2L, confirmed = true }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var releaseKey = Guid.NewGuid().ToString();
        using var released = await Mutate(owner, HttpMethod.Post, path + "/release", new { version = 1L, confirmed = true }, releaseKey);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        var releasedBody = await released.Content.ReadAsStringAsync(ct);
        using var replayRelease = await Mutate(owner, HttpMethod.Post, path + "/release", new { version = 1L, confirmed = true }, releaseKey);
        Assert.Equal(releasedBody, await replayRelease.Content.ReadAsStringAsync(ct));
        using var replayApply = await Mutate(owner, HttpMethod.Post, path + "/apply", body, key);
        Assert.Equal(original, await replayApply.Content.ReadAsStringAsync(ct));
        using var head = await owner.GetAsync(path, ct); var state = await head.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2L, state.GetProperty("version").GetInt64()); Assert.Equal("released", state.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("document", null)]
    [InlineData("document", "future.category")]
    public async Task PRD_70_HTTP_broad_hold_requires_confirmation_and_strict_schema_before_any_write(string? recordClass, string? category)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct); await RegisterAndLogin(foreign);
        var path = $"/organizations/{f.Organization}/retention-holds/{Guid.NewGuid()}";
        var key = Guid.NewGuid().ToString();
        var body = new { recordClass, category, recordId = (Guid?)null, reason = "Broad human hold", review = (object?)null, confirmed = false };
        using var notConfirmed = await Mutate(owner, HttpMethod.Post, path + "/apply", body, key);
        Assert.Equal(HttpStatusCode.BadRequest, notConfirmed.StatusCode);
        using var missing = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var foreignInvalid = await Mutate(foreign, HttpMethod.Post, path + "/apply", new { unexpected = true }, key);
        Assert.Equal(HttpStatusCode.NotFound, foreignInvalid.StatusCode);
        var confirmed = body with { confirmed = true };
        using var applied = await Mutate(owner, HttpMethod.Post, path + "/apply", confirmed, key);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        using var wrongActor = new HttpRequestMessage(HttpMethod.Get, path);
        wrongActor.Headers.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString());
        using var fenced = await owner.SendAsync(wrongActor, ct); Assert.Equal(HttpStatusCode.Unauthorized, fenced.StatusCode);
        using var unconfirmedRelease = await Mutate(owner, HttpMethod.Post, path + "/release", new { version = 1L, confirmed = false }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmedRelease.StatusCode);
        using var unknownField = await Mutate(owner, HttpMethod.Post, path + "/release", new { version = 1L, confirmed = true, actorId = Guid.NewGuid() }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);
        using var current = await owner.GetAsync(path, ct); var state = await current.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("active", state.GetProperty("status").GetString());
    }
}
