using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_15_FR_010_Http_account_rename_and_legal_deactivation_preserve_interpretable_activity()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var teammate = app.CreateClient();
        var f = await NotificationFixture(app, owner, teammate, ct);
        const string caption = "Former teammate <script>🙂";
        var profile = await teammate.GetFromJsonAsync<JsonElement>("/me", ct);
        using var named = await Mutate(teammate, HttpMethod.Patch, "/me", new { displayName = caption, version = profile.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Historical actor" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var authored = await Mutate(teammate, HttpMethod.Patch, $"/cards/{card}", new { title = "Authored by teammate", version = 1 });
        Assert.Equal(HttpStatusCode.OK, authored.StatusCode);
        var path = $"/cards/{card}/activity";
        var before = await owner.GetFromJsonAsync<JsonElement>(path, ct);
        var original = Assert.Single(before.GetProperty("items").EnumerateArray(), row => row.GetProperty("actorId").GetGuid() == f.Recipient);
        Assert.Equal(caption, original.GetProperty("actorLabel").GetString());
        var eventId = original.GetProperty("eventId").GetGuid();
        profile = await teammate.GetFromJsonAsync<JsonElement>("/me", ct);
        using var renamed = await Mutate(teammate, HttpMethod.Patch, "/me", new { displayName = "Current renamed profile", version = profile.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        // This teammate owns no Organization; the actual ownership admission
        // permits deactivation without bypassing sole-owner continuity.
        using var deactivated = await Mutate(teammate, HttpMethod.Post, "/me/deactivate", new { });
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        using var denied = await teammate.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        foreach (var target in new[] { path, $"/boards/{f.Board}/activity" })
        {
            var after = await owner.GetFromJsonAsync<JsonElement>(target, ct);
            var retained = Assert.Single(after.GetProperty("items").EnumerateArray(), row => row.GetProperty("eventId").GetGuid() == eventId);
            Assert.Equal(original.ToString(), retained.ToString()); Assert.Empty(retained.GetProperty("metadata").EnumerateObject());
        }
    }
}
