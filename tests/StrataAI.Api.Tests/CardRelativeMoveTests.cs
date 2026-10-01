using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Relative_card_moves_use_current_neighbors_and_bind_the_anchor_in_retry_receipts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        async Task<JsonElement> Create(string path, object body)
        {
            using var result = await Mutate(owner, HttpMethod.Post, path, body);
            Assert.Equal(HttpStatusCode.Created, result.StatusCode); return await result.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        var list = (await Create($"/boards/{board}/lists", new { name = "Ordering" })).GetProperty("id").GetGuid();
        var first = await Create($"/lists/{list}/cards", new { title = "First" });
        var anchor = await Create($"/lists/{list}/cards", new { title = "Anchor" });
        var moving = await Create($"/lists/{list}/cards", new { title = "Moving" });
        var card = moving.GetProperty("id").GetGuid(); var before = anchor.GetProperty("id").GetGuid(); var key = Guid.NewGuid().ToString();
        var body = new { destinationListId = list, beforeCardId = before, expectedVersion = 1 };
        using var placed = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var ack = await placed.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(string.CompareOrdinal(first.GetProperty("rank").GetString(), ack.GetProperty("rank").GetString()) < 0);
        Assert.True(string.CompareOrdinal(ack.GetProperty("rank").GetString(), anchor.GetProperty("rank").GetString()) < 0);
        using var self = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = list, beforeCardId = card, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var mixed = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = list, beforeCardId = before, rank = ack.GetProperty("rank").GetString(), expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        using var reused = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = list, beforeCardId = first.GetProperty("id").GetGuid(), expectedVersion = 1 }, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using var prepend = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = list, beforeCardId = first.GetProperty("id").GetGuid(), expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, prepend.StatusCode);
        var prepended = await prepend.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(string.CompareOrdinal(prepended.GetProperty("rank").GetString(), first.GetProperty("rank").GetString()) < 0);
        using var replay = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(ack.GetProperty("rank").GetString(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rank").GetString());
        var other = (await Create($"/boards/{board}/lists", new { name = "Other" })).GetProperty("id").GetGuid();
        var foreign = (await Create($"/lists/{other}/cards", new { title = "Wrong list" })).GetProperty("id").GetGuid();
        using var wrong = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = list, beforeCardId = foreign, expectedVersion = 3 });
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        var current = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var persisted = current.GetProperty("lists").EnumerateArray().Single(column => column.GetProperty("list").GetProperty("id").GetGuid() == list)
            .GetProperty("cards").EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == card);
        Assert.Equal(3, persisted.GetProperty("version").GetInt64());
        Assert.Equal(prepended.GetProperty("rank").GetString(), persisted.GetProperty("rank").GetString());
    }
}
