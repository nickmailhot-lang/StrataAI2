using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Relative_label_ordering_preserves_neighbors_and_recovers_original_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct); var labels = new List<JsonElement>();
        foreach (var name in new[] { "First", "Second", "Third" })
        {
            using var created = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name, color = "blue" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode); labels.Add(await created.Content.ReadFromJsonAsync<JsonElement>(ct));
        }
        var id = labels[2].GetProperty("id").GetGuid(); var anchor = labels[0].GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var body = new { beforeLabelId = anchor, version = 1 };
        using var moved = await Mutate(owner, HttpMethod.Post, $"/labels/{id}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode); var receipt = await moved.Content.ReadAsStringAsync(ct);
        var record = JsonSerializer.Deserialize<JsonElement>(receipt);
        Assert.Equal(2, record.GetProperty("version").GetInt64());
        Assert.True(string.CompareOrdinal(record.GetProperty("rank").GetString(), labels[0].GetProperty("rank").GetString()) < 0);
        var page = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/labels", ct);
        foreach (var original in labels.Take(2))
        {
            var unchanged = Assert.Single(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == original.GetProperty("id").GetGuid());
            Assert.Equal(original.GetRawText(), unchanged.GetRawText());
        }
        using var replay = await Mutate(owner, HttpMethod.Post, $"/labels/{id}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Post, $"/labels/{id}/move", body);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var last = await Mutate(owner, HttpMethod.Post, $"/labels/{id}/move", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        var lastRecord = await last.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(string.CompareOrdinal(lastRecord.GetProperty("rank").GetString(), labels[1].GetProperty("rank").GetString()) > 0);
        using var self = await Mutate(owner, HttpMethod.Post, $"/labels/{id}/move", new { beforeLabelId = id, version = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var denied = await Mutate(outsider, HttpMethod.Post, $"/labels/{id}/move", body, key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task Label_ordering_rejects_exhausted_rank_space_without_mutating_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var anchorResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "Anchor", color = "blue" });
        var anchor = (await anchorResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var movingResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "Moving", color = "red" });
        var moving = (await movingResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var setRank = await Mutate(owner, HttpMethod.Patch, $"/labels/{anchor}", new { name = "Anchor", color = "blue", rank = new string('0', 29) + "1", version = 1 });
        Assert.Equal(HttpStatusCode.OK, setRank.StatusCode);
        var before = await owner.GetStringAsync($"/boards/{board}/labels", ct);
        using var move = await Mutate(owner, HttpMethod.Post, $"/labels/{moving}/move", new { beforeLabelId = anchor, version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        Assert.Equal("rank_space_exhausted", (await move.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(before, await owner.GetStringAsync($"/boards/{board}/labels", ct));
    }
}
