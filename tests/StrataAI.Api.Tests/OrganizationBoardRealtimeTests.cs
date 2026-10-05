using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Ordinary_Organization_live_delivers_MEMBER_sources_isolates_archive_cursor_and_resets_after_private_withdrawal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        await store.UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Member, DateTimeOffset.UtcNow, ct);
        var board = await store.FindBoardAsync(f.Board, ct); Assert.NotNull(board);
        if (board.Visibility != BoardVisibility.Private)
            board = await store.SetBoardVisibilityAsync(f.Board, BoardVisibility.Private, board.Version, DateTimeOffset.UtcNow, ct);
        Assert.NotNull(board);
        using var socket = await LiveSocket(app, f.RecipientCookie, "/organizations/live");
        await SendFrame(socket, new { type = 4, invocationId = "reader", target = "WatchBoards", arguments = new string?[] { f.Organization.ToString(), null } });
        var initial = await StreamItem(socket);
        Assert.Equal(f.Recipient, initial.GetProperty("userId").GetGuid());
        Assert.True(initial.GetProperty("page").GetProperty("resetRequired").GetBoolean());
        using var archive = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = board.Version });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var page = (await StreamItem(socket)).GetProperty("page");
        var change = Assert.Single(page.GetProperty("events").EnumerateArray());
        Assert.Equal(f.Board, change.GetProperty("boardId").GetGuid());
        Assert.Equal("BOARD_ARCHIVED", change.GetProperty("eventType").GetString());
        var cursor = page.GetProperty("cursor").GetString();
        using var other = await LiveSocket(app, f.RecipientCookie, "/organizations/live");
        await SendFrame(other, new { type = 4, invocationId = "archive", target = "Watch", arguments = new[] { f.Organization.ToString(), cursor } });
        var rejected = (await StreamItem(other)).GetProperty("page");
        Assert.True(rejected.GetProperty("resetRequired").GetBoolean()); Assert.Empty(rejected.GetProperty("events").EnumerateArray());
        await store.RemoveBoardMemberAsync(f.Board, f.Recipient, DateTimeOffset.UtcNow, ct);
        var withdrawn = (await StreamItem(socket)).GetProperty("page");
        Assert.True(withdrawn.GetProperty("resetRequired").GetBoolean()); Assert.Empty(withdrawn.GetProperty("events").EnumerateArray());
        using var restore = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/restore", new { version = board.Version + 1 });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        using var resumed = await LiveSocket(app, f.RecipientCookie, "/organizations/live");
        await SendFrame(resumed, new { type = 4, invocationId = "withdrawn", target = "WatchBoards", arguments = new[] { f.Organization.ToString(), withdrawn.GetProperty("cursor").GetString() } });
        Assert.Empty((await StreamItem(resumed)).GetProperty("page").GetProperty("events").EnumerateArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://evil.example.test")]
    public async Task Organization_live_refuses_missing_or_foreign_origin(string? origin)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/organizations/live/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("realtime_origin_denied", (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken))!.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Organization_live_delivers_canonical_archive_replays_restore_and_resets_after_admin_demotion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        await app.Services.GetRequiredService<IWorkManagementStore>().UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Admin, DateTimeOffset.UtcNow, ct);
        using var socket = await LiveSocket(app, f.RecipientCookie, "/organizations/live");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { f.Organization.ToString(), null } });
        var initial = await StreamItem(socket);
        Assert.Equal(f.Organization, initial.GetProperty("organizationId").GetGuid());
        Assert.Equal(f.Recipient, initial.GetProperty("userId").GetGuid());
        Assert.True(initial.GetProperty("page").GetProperty("resetRequired").GetBoolean());
        Assert.Empty(initial.GetProperty("page").GetProperty("events").EnumerateArray());
        Assert.False(long.TryParse(initial.GetProperty("page").GetProperty("cursor").GetString(), out _));
        using var archive = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var archived = (await StreamItem(socket)).GetProperty("page");
        var change = Assert.Single(archived.GetProperty("events").EnumerateArray());
        Assert.Equal(f.Board, change.GetProperty("boardId").GetGuid());
        Assert.Equal("BOARD_ARCHIVED", change.GetProperty("eventType").GetString());
        Assert.Equal(new[] { "boardId", "createdAt", "eventId", "eventType", "version" }, change.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(2, change.GetProperty("version").GetInt64());
        var cursor = archived.GetProperty("cursor").GetString();
        socket.Dispose();
        using var restore = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        using var resumed = await LiveSocket(app, f.RecipientCookie, "/organizations/live");
        await SendFrame(resumed, new { type = 4, invocationId = "resume", target = "Watch", arguments = new[] { f.Organization.ToString(), cursor } });
        var restored = (await StreamItem(resumed)).GetProperty("page");
        Assert.False(restored.GetProperty("resetRequired").GetBoolean());
        Assert.Equal("BOARD_RESTORED", Assert.Single(restored.GetProperty("events").EnumerateArray()).GetProperty("eventType").GetString());
        await app.Services.GetRequiredService<IWorkManagementStore>().UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Member, DateTimeOffset.UtcNow, ct);
        var demoted = (await StreamItem(resumed)).GetProperty("page");
        Assert.True(demoted.GetProperty("resetRequired").GetBoolean()); Assert.Empty(demoted.GetProperty("events").EnumerateArray());
        Assert.Equal(OrganizationRemoveMemberResult.Removed, await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct));
        await AssertStarStreamClosed(resumed, ct);
    }

    [Fact]
    public async Task Organization_live_bounds_subscriptions_releases_cancel_and_stops_after_logout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        using var socket = await LiveSocket(app, f.RecipientCookie, "/organizations/live");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { f.Organization.ToString(), null } });
        var initial = (await StreamItem(socket)).GetProperty("page");
        await SendFrame(socket, new { type = 4, invocationId = "duplicate", target = "Watch", arguments = new string?[] { f.Organization.ToString(), null } });
        Assert.Contains("subscription_limit", (await Frame(socket, ct))!.Value.GetProperty("error").GetString());
        await SendFrame(socket, new { type = 5, invocationId = "watch" });
        Assert.Equal(3, (await Frame(socket, ct))!.Value.GetProperty("type").GetInt32());
        await SendFrame(socket, new { type = 4, invocationId = "replacement", target = "Watch", arguments = new[] { f.Organization.ToString(), initial.GetProperty("cursor").GetString() } });
        Assert.False((await StreamItem(socket)).GetProperty("page").GetProperty("resetRequired").GetBoolean());
        using var logout = await Mutate(member, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await AssertStarStreamClosed(socket, ct);
    }
}
