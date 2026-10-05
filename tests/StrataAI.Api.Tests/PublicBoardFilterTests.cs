using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Public_visitors_filter_admitted_active_Cards_without_member_discovery_or_writes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var visitor = app.CreateClient();
        var board = await TelemetryBoard(owner, ct);
        using var listReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Public filters" });
        var list = (await listReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardReply = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Public 100%_ match" });
        var card = (await cardReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var labelReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "Public priority", color = "red" });
        var label = (await labelReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var assign = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/labels/{label}?version=1", new { });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        foreach (var path in new[] { $"/boards/{board}/cards?match=invalid", $"/boards/{board}/labels?after=invalid" })
        {
            using var denied = await visitor.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.DoesNotContain("Public priority", await denied.Content.ReadAsStringAsync(ct));
        }
        using var publish = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 1 });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var before = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var result = await visitor.GetFromJsonAsync<JsonElement>($"/boards/{board}/cards?keyword=100%25_&labels={label}&match=all&due=none&completion=incomplete&activity=day", ct);
        Assert.Equal(card, Assert.Single(result.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("nextCursor").ValueKind);
        var labels = await visitor.GetFromJsonAsync<JsonElement>($"/boards/{board}/labels", ct);
        Assert.Equal(label, Assert.Single(labels.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.False(labels.GetProperty("canEdit").GetBoolean()); Assert.False(labels.GetProperty("canDelete").GetBoolean());
        using var invalid = await visitor.GetAsync($"/boards/{board}/cards?match=invalid", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_board_filter", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        foreach (var member in new[] { Guid.NewGuid().ToString(), "invalid" })
        {
            using var denied = await visitor.GetAsync($"/boards/{board}/cards?members={member}", ct);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.DoesNotContain("Public 100%_", await denied.Content.ReadAsStringAsync(ct));
        }
        var after = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        using var withdraw = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PRIVATE", version = 2 });
        Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);
        using var unavailable = await visitor.GetAsync($"/boards/{board}/cards?keyword=100%25_", ct);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        Assert.DoesNotContain("Public 100%_", await unavailable.Content.ReadAsStringAsync(ct));
    }
}
