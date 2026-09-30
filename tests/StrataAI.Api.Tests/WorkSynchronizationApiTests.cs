using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Sync_replays_versioned_events_with_stable_ids_bounded_pages_and_no_content()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var visitor = app.CreateClient();
        await RegisterAndLogin(owner);
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Sync organization" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Protected name", visibility = "PUBLIC" });
        var board = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var listed = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Protected list name" });
        var list = (await listed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var cardResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Protected title", description = "Private description" });
        var card = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var route = $"/boards/{board}/sync";
        using var first = await owner.GetAsync(route + "?limit=2", ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("2", firstJson.GetProperty("cursor").GetString()); Assert.True(firstJson.GetProperty("hasMore").GetBoolean());
        using var replay = await owner.GetAsync(route + "?limit=2", ct);
        Assert.Equal(firstJson.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetRawText());
        using var publicPage = await visitor.GetAsync(route + "?since=2", ct);
        var publicJson = await publicPage.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var change = Assert.Single(publicJson.GetProperty("events").EnumerateArray());
        Assert.Equal(card, change.GetProperty("entityId").GetGuid()); Assert.Equal("CARD_CREATED", change.GetProperty("eventType").GetString());
        Assert.Equal(JsonValueKind.Null, change.GetProperty("actorId").ValueKind);
        Assert.DoesNotContain("Private description", publicJson.GetRawText()); Assert.DoesNotContain("Protected title", publicJson.GetRawText());
        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var historical = await visitor.GetAsync(route + "?since=2", ct);
        var history = await historical.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.DoesNotContain(card.ToString(), history.GetRawText());
        Assert.All(history.GetProperty("events").EnumerateArray(), item =>
        { Assert.Equal("BOARD_INVALIDATED", item.GetProperty("eventType").GetString()); Assert.Equal(board, item.GetProperty("entityId").GetGuid()); });
        using var reset = await owner.GetAsync(route + "?since=9223372036854775807", ct);
        var resetJson = await reset.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.True(resetJson.GetProperty("resetRequired").GetBoolean()); Assert.Equal("0", resetJson.GetProperty("cursor").GetString());
    }

    [Theory]
    [InlineData("since=-1", "invalid_sync_cursor")]
    [InlineData("since=abc", "invalid_sync_cursor")]
    [InlineData("since=9223372036854775808", "invalid_sync_cursor")]
    [InlineData("since=1&since=2", "invalid_sync_cursor")]
    [InlineData("since=1%00", "invalid_sync_cursor")]
    [InlineData("limit=0", "invalid_sync_limit")]
    [InlineData("limit=101", "invalid_sync_limit")]
    public async Task Sync_rejects_invalid_cursor_or_unbounded_page_without_disclosure(string query, string code)
    {
        await using var app = new ApiFactory(); using var visitor = app.CreateClient();
        using var response = await visitor.GetAsync($"/boards/{Guid.NewGuid()}/sync?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sync_denies_private_board_and_sanitizes_adapter_failures()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IWorkEventReader>(new BrokenReader()));
        using var owner = app.CreateClient(); using var visitor = app.CreateClient();
        await RegisterAndLogin(owner);
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Private sync" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Hidden board" });
        var board = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var denied = await visitor.GetAsync($"/boards/{board}/sync", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Hidden board", await denied.Content.ReadAsStringAsync(ct));
        using var failed = await owner.GetAsync($"/boards/{board}/sync", ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var safe = await failed.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("secret", safe); Assert.DoesNotContain("SELECT", safe);
        Assert.Equal("work_sync_unavailable", (await failed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("code").GetString());
    }

    private sealed class BrokenReader : IWorkEventReader
    {
        public Task<WorkEventReadPage> ReadAsync(Guid organizationId, Guid boardId, long since, int limit, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SELECT private_payload secret-password");
    }
}
