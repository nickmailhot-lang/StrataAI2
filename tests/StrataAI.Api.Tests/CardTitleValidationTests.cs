using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task PRD_08_Card_creation_rejects_blank_title_without_persisting_a_Card(string title)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        var scope = await CreateCardTitleValidationScopeAsync(owner, ct);

        using var refused = await Mutate(owner, HttpMethod.Post, $"/lists/{scope.List}/cards", new { title });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("invalid_card_title", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("code").GetString());
        var unchanged = await owner.GetFromJsonAsync<JsonElement>($"/boards/{scope.Board}", ct);
        Assert.Empty(Assert.Single(unchanged.GetProperty("lists").EnumerateArray()).GetProperty("cards").EnumerateArray());

        using var accepted = await Mutate(owner, HttpMethod.Post, $"/lists/{scope.List}/cards", new { title = "Validated Card" });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var created = await accepted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var canonical = await owner.GetFromJsonAsync<JsonElement>($"/boards/{scope.Board}", ct);
        var only = Assert.Single(Assert.Single(canonical.GetProperty("lists").EnumerateArray()).GetProperty("cards").EnumerateArray());
        Assert.Equal(created.GetProperty("id").GetGuid(), only.GetProperty("id").GetGuid());
        Assert.Equal("Validated Card", only.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(only.GetProperty("rank").GetString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task PRD_08_Card_rename_rejects_blank_title_without_changing_the_original_Card(string title)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        var scope = await CreateCardTitleValidationScopeAsync(owner, ct);
        using var creation = await Mutate(owner, HttpMethod.Post, $"/lists/{scope.List}/cards", new { title = "Original Card", description = "Preserved description" });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var original = await creation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var id = original.GetProperty("id").GetGuid();
        var version = original.GetProperty("version").GetInt64();
        var before = await owner.GetFromJsonAsync<JsonElement>($"/boards/{scope.Board}", ct);
        var beforeCard = Assert.Single(Assert.Single(before.GetProperty("lists").EnumerateArray()).GetProperty("cards").EnumerateArray());
        Assert.Equal(id, beforeCard.GetProperty("id").GetGuid());
        Assert.Equal(version, beforeCard.GetProperty("version").GetInt64());
        Assert.Equal("Original Card", beforeCard.GetProperty("title").GetString());
        Assert.Equal("Preserved description", beforeCard.GetProperty("description").GetString());

        using var refused = await Mutate(owner, HttpMethod.Patch, $"/cards/{id}", new { title, description = "Rejected change", version });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("invalid_card_title", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("code").GetString());
        var unchanged = await owner.GetFromJsonAsync<JsonElement>($"/boards/{scope.Board}", ct);
        var only = Assert.Single(Assert.Single(unchanged.GetProperty("lists").EnumerateArray()).GetProperty("cards").EnumerateArray());
        Assert.Equal(beforeCard.GetRawText(), only.GetRawText());

        using var accepted = await Mutate(owner, HttpMethod.Patch, $"/cards/{id}", new { title = "Renamed Card", description = "Preserved description", version });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var canonical = await owner.GetFromJsonAsync<JsonElement>($"/boards/{scope.Board}", ct);
        var renamed = Assert.Single(Assert.Single(canonical.GetProperty("lists").EnumerateArray()).GetProperty("cards").EnumerateArray());
        Assert.Equal(id, renamed.GetProperty("id").GetGuid());
        Assert.Equal("Renamed Card", renamed.GetProperty("title").GetString());
        Assert.Equal(version + 1, renamed.GetProperty("version").GetInt64());
        Assert.Equal(original.GetProperty("rank").GetString(), renamed.GetProperty("rank").GetString());
        Assert.Equal("Preserved description", renamed.GetProperty("description").GetString());
    }

    private static async Task<(Guid Board, Guid List)> CreateCardTitleValidationScopeAsync(HttpClient owner, CancellationToken ct)
    {
        await RegisterAndLogin(owner);
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Card title validation Organization" });
        Assert.Equal(HttpStatusCode.Created, organization.StatusCode);
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Card title validation Board" });
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Validated planning" });
        Assert.Equal(HttpStatusCode.Created, listResponse.StatusCode);
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        return (board, list);
    }
}
