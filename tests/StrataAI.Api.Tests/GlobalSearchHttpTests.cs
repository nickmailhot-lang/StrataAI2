using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Global_search_HTTP_returns_authorized_context_and_masks_private_content_and_binds_cursor()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var board = await TelemetryBoard(owner, ct);
        using var listReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Search List" });
        var list = (await listReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardReply = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards",
            new { title = "Private search result", description = "Literal 100%_ needle" });
        var card = (await cardReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var read = await owner.GetAsync("/search?q=100%25_&scope=active&match=ALL", ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl!.Private); Assert.True(read.Headers.CacheControl.NoStore);
        var page = await read.Content.ReadFromJsonAsync<JsonElement>(ct);
        var document = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(card, document.GetProperty("card").GetProperty("id").GetGuid());
        Assert.Equal("Search List", document.GetProperty("listName").GetString());
        Assert.Equal("CARD", document.GetProperty("sourceKind").GetString());
        var cursor = page.GetProperty("nextCursor").GetString(); Assert.False(string.IsNullOrEmpty(cursor));
        using var otherActorCursor = await outsider.GetAsync($"/search?q=100%25_&after={Uri.EscapeDataString(cursor!)}", ct);
        Assert.Equal(HttpStatusCode.BadRequest, otherActorCursor.StatusCode);
        Assert.DoesNotContain("Private search result", await otherActorCursor.Content.ReadAsStringAsync(ct));
        using var tail = await owner.GetAsync($"/search?q=100%25_&after={Uri.EscapeDataString(cursor!)}", ct);
        Assert.Equal(HttpStatusCode.OK, tail.StatusCode);
        var tailPage = await tail.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Empty(tailPage.GetProperty("items").EnumerateArray()); Assert.Equal(JsonValueKind.Null, tailPage.GetProperty("nextCursor").ValueKind);
        foreach (var client in new[] { owner, outsider })
        {
            using var changed = await client.GetAsync($"/search?q=changed&after={Uri.EscapeDataString(cursor!)}", ct);
            Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
            Assert.Equal("invalid_search", (await changed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            Assert.DoesNotContain("Private search result", await changed.Content.ReadAsStringAsync(ct));
        }
        using var hidden = await outsider.GetAsync("/search?q=needle", ct);
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        Assert.Empty((await hidden.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray());
        using var unauthenticated = await anonymous.GetAsync("/search?q=needle", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        foreach (var query in new[] { "match=bad", "scope=bad", "after=bad", $"q={new string('x', 161)}" })
        {
            using var invalid = await owner.GetAsync($"/search?{query}", ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_search", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
        using var archive = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>("/search?q=needle", ct)).GetProperty("items").EnumerateArray());
        var archived = await owner.GetFromJsonAsync<JsonElement>("/search?q=needle&scope=archived", ct);
        Assert.Equal(card, Assert.Single(archived.GetProperty("items").EnumerateArray()).GetProperty("card").GetProperty("id").GetGuid());
    }
}
