using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task List_prepend_and_exhausted_boundary_preserve_other_lists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        async Task<JsonElement> Create(string name, string? rank = null)
        {
            using var result = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name, rank });
            Assert.Equal(HttpStatusCode.Created, result.StatusCode);
            return await result.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        var first = await Create("First"); var moving = await Create("Moving");
        var id = moving.GetProperty("id").GetGuid();
        using var prepend = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}",
            new { name = "Moving", beforeListId = first.GetProperty("id").GetGuid(), version = 1 });
        Assert.Equal(HttpStatusCode.OK, prepend.StatusCode);
        var placed = await prepend.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(string.CompareOrdinal(placed.GetProperty("rank").GetString(), first.GetProperty("rank").GetString()) < 0);
        var edge = await Create("Lowest rank", "000000000000000000000000000001");
        var key = Guid.NewGuid().ToString();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var exhausted = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}",
                new { name = "Moving", beforeListId = edge.GetProperty("id").GetGuid(), version = 2 }, key);
            Assert.Equal(HttpStatusCode.Conflict, exhausted.StatusCode);
            Assert.Equal("rank_space_exhausted", (await exhausted.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
        using var mixed = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}",
            new { name = "Moving", rank = placed.GetProperty("rank").GetString(), moveToEnd = true, version = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        using var empty = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}",
            new { name = "Moving", beforeListId = Guid.Empty, version = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var current = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var lists = current.GetProperty("lists").EnumerateArray().Select(column => column.GetProperty("list")).ToArray();
        foreach (var expected in new[] { first, placed, edge })
        {
            var actual = lists.Single(list => list.GetProperty("id").GetGuid() == expected.GetProperty("id").GetGuid());
            Assert.Equal(expected.GetProperty("rank").GetString(), actual.GetProperty("rank").GetString());
            Assert.Equal(expected.GetProperty("version").GetInt64(), actual.GetProperty("version").GetInt64());
        }
    }

    [Fact]
    public async Task List_positions_use_current_neighbors_and_do_not_reapply_historical_receipts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        async Task<JsonElement> Create(string name)
        {
            using var result = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name });
            Assert.Equal(HttpStatusCode.Created, result.StatusCode);
            return await result.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        var first = await Create("First"); var anchor = await Create("Anchor"); var moving = await Create("Moving");
        var id = moving.GetProperty("id").GetGuid(); var before = anchor.GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var body = new { name = "Moving", beforeListId = before, version = 1 };
        using var placed = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", body, key);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var ack = await placed.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(string.CompareOrdinal(first.GetProperty("rank").GetString(), ack.GetProperty("rank").GetString()) < 0);
        Assert.True(string.CompareOrdinal(ack.GetProperty("rank").GetString(), anchor.GetProperty("rank").GetString()) < 0);
        using var mixed = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", new { name = "Moving", beforeListId = before, moveToEnd = true, version = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        using var self = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", new { name = "Moving", beforeListId = id, version = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var reused = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", new { name = "Moving", moveToEnd = true, version = 1 }, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using var end = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", new { name = "Moving", moveToEnd = true, version = 2 });
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        var ended = await end.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(string.CompareOrdinal(anchor.GetProperty("rank").GetString(), ended.GetProperty("rank").GetString()) < 0);
        using var replay = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(ack.GetProperty("rank").GetString(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rank").GetString());
        using var missing = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", new { name = "Moving", beforeListId = Guid.NewGuid(), version = 3 });
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Patch, $"/lists/{id}", new { name = "Moving", moveToEnd = true, version = 2 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var lists = current.GetProperty("lists").EnumerateArray().Select(column => column.GetProperty("list")).ToArray();
        var persisted = lists.Single(list => list.GetProperty("id").GetGuid() == id);
        Assert.Equal(3, persisted.GetProperty("version").GetInt64());
        Assert.Equal(ended.GetProperty("rank").GetString(), persisted.GetProperty("rank").GetString());
        foreach (var original in new[] { first, anchor })
        {
            var sibling = lists.Single(list => list.GetProperty("id").GetGuid() == original.GetProperty("id").GetGuid());
            Assert.Equal(original.GetProperty("rank").GetString(), sibling.GetProperty("rank").GetString());
            Assert.Equal(1, sibling.GetProperty("version").GetInt64());
        }
    }
}
