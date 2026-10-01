using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-06/08: server appends without client rank calculations; retry never reapplies.
    [Fact]
    public async Task Default_card_move_appends_and_replays_the_original_ack_without_reapplying_after_a_later_move()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        async Task<JsonElement> Create(string path, object body)
        {
            using var result = await Mutate(owner, HttpMethod.Post, path, body);
            Assert.Equal(HttpStatusCode.Created, result.StatusCode);
            return await result.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        var source = (await Create($"/boards/{board}/lists", new { name = "Source" })).GetProperty("id").GetGuid();
        var destination = (await Create($"/boards/{board}/lists", new { name = "Destination" })).GetProperty("id").GetGuid();
        var card = (await Create($"/lists/{source}/cards", new { title = "Move this" })).GetProperty("id").GetGuid();
        var last = await Create($"/lists/{destination}/cards", new { title = "Existing tail" });
        var key = Guid.NewGuid().ToString();
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = destination, expectedVersion = 1 }, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var ack = await moved.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(destination, ack.GetProperty("listId").GetGuid());
        Assert.True(string.CompareOrdinal(ack.GetProperty("rank").GetString(), last.GetProperty("rank").GetString()) > 0);
        Assert.Equal(2, ack.GetProperty("version").GetInt64());
        using var later = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = source, rank = RankToken.Initial(), expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = destination, expectedVersion = 1 }, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(ack.GetProperty("rank").GetString(), replayed.GetProperty("rank").GetString());
        Assert.Equal(2, replayed.GetProperty("version").GetInt64());
        var snapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var current = snapshot.GetProperty("lists").EnumerateArray().Single(column => column.GetProperty("list").GetProperty("id").GetGuid() == source)
            .GetProperty("cards").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == card);
        Assert.Equal(3, current.GetProperty("version").GetInt64());
        using var stale = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = destination, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = destination, rank = "", expectedVersion = 3 });
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        // Excluding the moving card allows append within a one-card list without using its own tail.
        using var self = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = source, rank = (string?)null, expectedVersion = 3 });
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        var selfAck = await self.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(RankToken.Initial(), selfAck.GetProperty("rank").GetString());
        Assert.Equal(4, selfAck.GetProperty("version").GetInt64());
    }
}
