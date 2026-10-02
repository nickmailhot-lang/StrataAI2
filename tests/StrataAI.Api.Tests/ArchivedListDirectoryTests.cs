using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Archived_lists_are_bounded_scoped_read_only_and_count_contained_cards()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner); var board = await TelemetryBoard(owner, ct);
        var expected = new List<Guid>(); Guid first = default;
        for (var index = 0; index < 51; index++)
        {
            using var created = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = $"Archived {index}" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            if (index == 0)
            {
                first = id;
                using var card = await Mutate(owner, HttpMethod.Post, $"/lists/{id}/cards", new { title = "Contained card" });
                Assert.Equal(HttpStatusCode.Created, card.StatusCode);
            }
            using var archived = await Mutate(owner, HttpMethod.Post, $"/lists/{id}/archive", new { version = 1 });
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode); expected.Add(id);
        }
        using var active = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Active" });
        Assert.Equal(HttpStatusCode.Created, active.StatusCode);
        var page = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-lists", ct);
        Assert.Equal(board, page.GetProperty("boardId").GetGuid());
        Assert.Equal(50, page.GetProperty("items").GetArrayLength());
        var cursor = page.GetProperty("nextCursor").GetGuid();
        var second = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-lists?after={cursor}", ct);
        Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var items = page.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(expected.Order(), items.Select(item => item.GetProperty("list").GetProperty("id").GetGuid()));
        Assert.All(items, item => Assert.Equal("archived", item.GetProperty("list").GetProperty("lifecycleState").GetString()));
        Assert.Equal(1, items.Single(item => item.GetProperty("list").GetProperty("id").GetGuid() == first).GetProperty("containedCardCount").GetInt64());
        Assert.Equal(0, items.Where(item => item.GetProperty("list").GetProperty("id").GetGuid() != first).Sum(item => item.GetProperty("containedCardCount").GetInt64()));
        var canvas = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Single(canvas.GetProperty("lists").EnumerateArray());
        using var invalid = await owner.GetAsync($"/boards/{board}/archived-lists?after=not-a-uuid", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_archive_cursor", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var denied = await outsider.GetAsync($"/boards/{board}/archived-lists?after=not-a-uuid", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Archived 0", await denied.Content.ReadAsStringAsync(ct));
        using var anonymous = app.CreateClient();
        using var loggedOut = await anonymous.GetAsync($"/boards/{board}/archived-lists", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        using var restored = await Mutate(owner, HttpMethod.Post, $"/lists/{first}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-lists", ct);
        Assert.Equal(50, after.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("nextCursor").ValueKind);
        Assert.DoesNotContain(after.GetProperty("items").EnumerateArray(), item => item.GetProperty("list").GetProperty("id").GetGuid() == first);
    }
}
