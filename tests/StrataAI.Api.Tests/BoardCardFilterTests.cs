using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Board_filters_use_all_associations_literal_keywords_and_ANY_ALL_without_cross_Board_disclosure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var listReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Filter list" });
        var list = (await listReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardReply = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Literal 100%_ Ready", description = "Hidden description keyword" });
        var card = (await cardReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var otherReply = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Other Card" });
        var other = (await otherReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var labels = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            using var labelReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = $"Filter {i}", color = "blue" });
            var id = (await labelReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid(); labels.Add(id);
            using var assign = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/labels/{id}?version={i + 1}", new { }); Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        }
        using var otherAssign = await Mutate(owner, HttpMethod.Put, $"/cards/{other}/labels/{labels[7]}?version=1", new { }); Assert.Equal(HttpStatusCode.OK, otherAssign.StatusCode);
        async Task<Guid[]> Read(string query) => (await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/cards?{query}", ct))
            .GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(new[] { card }, await Read($"labels={labels[0]},{labels[7]}&match=all"));
        Assert.Equal(new[] { card, other }.Order(), (await Read($"labels={labels[0]},{labels[7]}&match=any")).Order());
        Assert.Equal(new[] { card }, await Read("keyword=100%25_&match=all"));
        Assert.Equal(new[] { card }, await Read("keyword=DESCRIPTION"));
        Assert.Empty(await Read($"keyword=absent&labels={labels[7]}&match=all"));
        Assert.Equal(2, (await Read($"keyword=absent&labels={labels[7]}&match=any")).Length);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var memberAssign = await Mutate(owner, HttpMethod.Put, $"/cards/{other}/members/{actor}?version=2", new { }); Assert.Equal(HttpStatusCode.OK, memberAssign.StatusCode);
        Assert.Empty(await Read($"members={actor}&labels={labels[0]}&match=all"));
        Assert.Equal(new[] { card, other }.Order(), (await Read($"members={actor}&labels={labels[0]}&match=any")).Order());
        Assert.Equal(new[] { other }, await Read($"members={actor}&labels={labels[7]}&match=all"));
        using var invalidMembers = await owner.GetAsync($"/boards/{board}/cards?members=bad", ct); Assert.Equal(HttpStatusCode.BadRequest, invalidMembers.StatusCode);
        // A foreign/missing label is an unsatisfied predicate, never a discovery lookup.
        var foreignBoard = await TelemetryBoard(owner, ct);
        using var foreignReply = await Mutate(owner, HttpMethod.Post, $"/boards/{foreignBoard}/labels", new { name = "Other Board metadata", color = "red" });
        var foreignLabel = (await foreignReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        Assert.Empty(await Read($"labels={labels[7]},{foreignLabel}&match=all"));
        Assert.Equal(new[] { card }, await Read($"labels={labels[0]},{foreignLabel}&match=any"));
        Assert.Empty(await Read($"labels={Guid.NewGuid()}&match=all"));
        using var delete = await Mutate(owner, HttpMethod.Delete, $"/labels/{labels[7]}?version=1&confirmed=true", new { }); Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Empty(await Read($"labels={labels[7]}"));
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var denied = await outsider.GetAsync($"/boards/{board}/cards?labels=invalid&match=invalid", ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var publish = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 1 }); Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using var publicMembers = await outsider.GetAsync($"/boards/{board}/cards?members={actor}", ct); Assert.Equal(HttpStatusCode.NotFound, publicMembers.StatusCode);
        Assert.DoesNotContain("Other Card", await publicMembers.Content.ReadAsStringAsync(ct));
        using var archive = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 }); Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Empty(await Read(""));
        using var boardArchive = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/archive", new { version = 2 }); Assert.Equal(HttpStatusCode.OK, boardArchive.StatusCode);
        using var unavailable = await owner.GetAsync($"/boards/{board}/cards", ct); Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }

    [Fact]
    public async Task Board_filters_are_bounded_paginated_and_reject_invalid_input_after_admission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var listReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Paged filters" });
        var list = (await listReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        for (var i = 0; i < 52; i++) { using var card = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = $"Paged {i}" }); Assert.Equal(HttpStatusCode.Created, card.StatusCode); }
        var first = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/cards?keyword=Paged", ct);
        Assert.Equal(50, first.GetProperty("items").GetArrayLength()); var cursor = first.GetProperty("nextCursor").GetGuid();
        Assert.Equal(cursor, first.GetProperty("items")[49].GetProperty("id").GetGuid());
        var second = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/cards?keyword=Paged&after={cursor}", ct);
        Assert.Equal(2, second.GetProperty("items").GetArrayLength()); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(52, first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(c => c.GetProperty("id").GetGuid()).Distinct().Count());
        var id = Guid.NewGuid();
        foreach (var query in new[] { "labels=invalid", $"labels={id},{id}", "match=wrong", "after=invalid", "keyword=" + new string('x', 161), "labels=" + string.Join(',', Enumerable.Range(0, 26).Select(_ => Guid.NewGuid())) })
        {
            using var invalid = await owner.GetAsync($"/boards/{board}/cards?{query}", ct); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_board_filter", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
    }
}
