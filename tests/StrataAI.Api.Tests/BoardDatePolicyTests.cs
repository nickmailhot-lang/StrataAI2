using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Board_date_policy_is_admin_only_versioned_retry_safe_and_does_not_change_card_dates()
    {
        var ct = TestContext.Current.CancellationToken;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter<BoardVisibility>(JsonNamingPolicy.CamelCase));
        json.Converters.Add(new JsonStringEnumConverter<BoardLifecycleState>(JsonNamingPolicy.CamelCase));
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var board = (await store.FindBoardAsync(f.Board, ct))!;
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Policy dates", null, null, DateTimeOffset.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, "2040-01-02T12:00:00Z", "UTC", true, false, card.Version));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        card = (await store.FindCardAsync(card.Id, ct))!;
        var path = $"/boards/{f.Board}/date-policy";
        var input = new BoardDatePolicyInput("Pacific/Honolulu", board.Version);
        using var denied = await Mutate(member, HttpMethod.Patch, path, input);
        using var hidden = await Mutate(outsider, HttpMethod.Patch, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        foreach (var zone in new[] { "Unknown/Place", "Pacific Standard Time", " UTC", "" })
        {
            using var invalid = await Mutate(owner, HttpMethod.Patch, path, input with { Timezone = zone });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(board, await store.FindBoardAsync(f.Board, ct));
        var key = Guid.NewGuid().ToString();
        using var changed = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = await changed.Content.ReadFromJsonAsync<BoardDatePolicyChange>(json, ct);
        Assert.True(updated!.Changed); Assert.Equal("Pacific/Honolulu", updated.Board.DateTimezoneOverride);
        Assert.Equal(board.Version + 1, updated.Board.Version);
        using var stale = await Mutate(owner, HttpMethod.Patch, path, input with { Timezone = "UTC" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var noop = await Mutate(owner, HttpMethod.Patch, path, input with { Version = updated.Board.Version });
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        Assert.False((await noop.Content.ReadFromJsonAsync<BoardDatePolicyChange>(json, ct))!.Changed);
        using var clear = await Mutate(owner, HttpMethod.Patch, path, new BoardDatePolicyInput(null, updated.Board.Version));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = (await clear.Content.ReadFromJsonAsync<BoardDatePolicyChange>(json, ct))!;
        Assert.Null(cleared.Board.DateTimezoneOverride); Assert.Equal(updated.Board.Version + 1, cleared.Board.Version);
        using var replay = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await changed.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(cleared.Board, await store.FindBoardAsync(f.Board, ct));
        Assert.Equal(card, await store.FindCardAsync(card.Id, ct));
        using var archive = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = cleared.Board.Version });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var retiredReplay = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, retiredReplay.StatusCode);
    }
}
