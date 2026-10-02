using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task List_deletion_requires_current_explicit_impact_and_recovers_only_the_same_authorized_intent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var created = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Delete review" });
        var list = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var card = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Contained work" });
        Assert.Equal(HttpStatusCode.Created, card.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        async Task Reject(string query, HttpStatusCode status, string code)
        {
            using var response = await Mutate(owner, HttpMethod.Delete, $"/lists/{list}?version=2{query}", new { });
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            var page = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-lists", ct);
            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(2, item.GetProperty("list").GetProperty("version").GetInt64());
            Assert.Equal(1, item.GetProperty("containedCardCount").GetInt64());
        }
        await Reject("", HttpStatusCode.BadRequest, "delete_confirmation_required");
        await Reject("&confirmed=false&containedCardCount=1", HttpStatusCode.BadRequest, "delete_confirmation_required");
        await Reject("&confirmed=true", HttpStatusCode.BadRequest, "deletion_impact_required");
        await Reject("&confirmed=true&containedCardCount=-1", HttpStatusCode.BadRequest, "deletion_impact_required");
        await Reject("&confirmed=true&containedCardCount=0", HttpStatusCode.Conflict, "deletion_impact_changed");
        var key = Guid.NewGuid().ToString(); var path = $"/lists/{list}?version=2&confirmed=true&containedCardCount=1";
        string? receipt = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var deleted = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            var text = await deleted.Content.ReadAsStringAsync(ct);
            if (receipt is null) receipt = text; else Assert.Equal(receipt, text);
            var value = JsonSerializer.Deserialize<JsonElement>(text);
            Assert.Equal("deleted", value.GetProperty("lifecycleState").GetString()); Assert.Equal(3, value.GetProperty("version").GetInt64());
        }
        var empty = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-lists", ct);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        using var restore = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/restore", new { version = 3 });
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
        using var changed = await Mutate(owner, HttpMethod.Delete, $"/lists/{list}?version=2&confirmed=true&containedCardCount=0", new { }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("idempotency_key_reused", (await changed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var denied = await Mutate(outsider, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Delete review", await denied.Content.ReadAsStringAsync(ct));
        using var archivedBoard = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedBoard.StatusCode);
        using var inactiveReplay = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, inactiveReplay.StatusCode);
    }
}
