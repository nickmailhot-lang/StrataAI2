using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("card")]
    [InlineData("cardComment")]
    [InlineData("attachment")]
    public async Task PRD_70_HTTP_hold_review_admits_classified_sources_without_exposing_content_or_rewriting_classification(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct); await RegisterAndLogin(foreign);
        var id = kind == "card" ? f.Card : kind == "cardComment" ? f.Comment : f.Attachment;
        var holdReview = $"/organizations/{f.Organization}/retention-holds/sources/{kind}/{id}/review";
        var source = $"/organizations/{f.Organization}/retention-sources/{kind}/{id}";
        using var before = await owner.GetAsync(holdReview, ct); Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        using var original = await owner.GetAsync(source + "/review", ct); Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var evidence = await original.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var classified = await Mutate(owner, HttpMethod.Post, source + "/classification",
            new { review = evidence, category = "reviewed.http.hold", jurisdiction = (string?)null, confirmed = true }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, classified.StatusCode);
        using var actual = await owner.GetAsync(holdReview, ct); Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
        Assert.True(actual.Headers.CacheControl!.Private); Assert.True(actual.Headers.CacheControl.NoStore);
        var text = await actual.Content.ReadAsStringAsync(ct); using var body = JsonDocument.Parse(text);
        Assert.Equal(f.Organization, body.RootElement.GetProperty("organizationId").GetGuid());
        Assert.Equal(id, body.RootElement.GetProperty("recordId").GetGuid()); Assert.Equal(kind, body.RootElement.GetProperty("kind").GetString());
        Assert.Equal(f.Board, body.RootElement.GetProperty("boardId").GetGuid());
        Assert.DoesNotContain("Private held Card", text); Assert.DoesNotContain("Private retained comment", text);
        Assert.DoesNotContain("Private retained file", text); Assert.DoesNotContain("https://example.test", text);
        Assert.DoesNotContain("reviewed.http.hold", text);
        using var rewrite = await owner.GetAsync(source + "/review", ct); Assert.Equal(HttpStatusCode.NotFound, rewrite.StatusCode);
        using var denied = await foreign.GetAsync(holdReview, ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var refusal = await denied.Content.ReadAsStringAsync(ct); Assert.DoesNotContain(id.ToString(), refusal);
        Assert.DoesNotContain(f.Board.ToString(), refusal); Assert.Contains("retention_hold_not_found", refusal);
        using var wrongActor = new HttpRequestMessage(HttpMethod.Get, holdReview);
        wrongActor.Headers.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString());
        using var fenced = await owner.SendAsync(wrongActor, ct); Assert.Equal(HttpStatusCode.Unauthorized, fenced.StatusCode);
        using var unsupported = await owner.GetAsync($"/organizations/{f.Organization}/retention-holds/sources/auditEvent/{id}/review", ct);
        Assert.Equal(HttpStatusCode.NotFound, unsupported.StatusCode);
    }
}
