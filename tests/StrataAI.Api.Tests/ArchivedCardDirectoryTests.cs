using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Archived_cards_page_without_descriptions_and_require_live_parent_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner); var board = await TelemetryBoard(owner, ct);
        using var createdList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Archive parent" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var expected = new List<Guid>();
        for (var index = 0; index < 51; index++)
        {
            using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = $"Archived card {index}", description = "Private detail body" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{id}/archive", new { version = 1 });
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode); expected.Add(id);
        }
        using var active = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Active card" });
        Assert.Equal(HttpStatusCode.Created, active.StatusCode);
        var first = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-cards", ct);
        Assert.True(first.GetProperty("canDelete").GetBoolean());
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetGuid();
        var second = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-cards?after={cursor}", ct);
        Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var rows = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(expected.Order(), rows.Select(row => row.GetProperty("card").GetProperty("id").GetGuid()));
        Assert.All(rows, row => {
            Assert.Equal("archived", row.GetProperty("card").GetProperty("lifecycleState").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("card").GetProperty("description").ValueKind);
            Assert.Equal(list, row.GetProperty("list").GetProperty("id").GetGuid());
        });
        using var invalid = await owner.GetAsync($"/boards/{board}/archived-cards?after=bad", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var denied = await outsider.GetAsync($"/boards/{board}/archived-cards?after=bad", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Archive parent", await denied.Content.ReadAsStringAsync(ct));
        using var archivedList = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedList.StatusCode);
        var parentPage = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-cards", ct);
        Assert.All(parentPage.GetProperty("items").EnumerateArray(), row =>
            Assert.Equal("archived", row.GetProperty("list").GetProperty("lifecycleState").GetString()));
        using var restoreBlocked = await Mutate(owner, HttpMethod.Post, $"/cards/{expected[0]}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.NotFound, restoreBlocked.StatusCode);
        using var deletedList = await Mutate(owner, HttpMethod.Delete, $"/lists/{list}?version=2&confirmed=true&containedCardCount=52", new { });
        Assert.Equal(HttpStatusCode.OK, deletedList.StatusCode);
        var empty = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-cards", ct);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("nextCursor").ValueKind);
    }
}
