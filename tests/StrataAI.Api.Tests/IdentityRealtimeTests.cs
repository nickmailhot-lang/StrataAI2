using System.Net;
using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Identity_live_limits_subscriptions_and_releases_them_after_cancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var socket = await LiveSocket(app, cookie, "/me/live");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { null } });
        await StreamItem(socket);
        await SendFrame(socket, new { type = 4, invocationId = "duplicate", target = "Watch", arguments = new string?[] { null } });
        var rejected = await Frame(socket, ct);
        Assert.Contains("subscription_limit", rejected!.Value.GetProperty("error").GetString());
        await SendFrame(socket, new { type = 5, invocationId = "watch" });
        Assert.Equal(3, (await Frame(socket, ct))!.Value.GetProperty("type").GetInt32());
        await SendFrame(socket, new { type = 4, invocationId = "replacement", target = "Watch", arguments = new[] { "1" } });
        Assert.Equal(1, (await StreamItem(socket)).GetProperty("cursor").GetInt64());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("9223372036854775808")]
    public async Task Identity_live_rejects_invalid_cursors(string cursor)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var socket = await LiveSocket(app, cookie, "/me/live");
        await SendFrame(socket, new { type = 4, invocationId = "invalid", target = "Watch", arguments = new[] { cursor } });
        var rejected = await Frame(socket, ct);
        Assert.Contains("invalid_identity_cursor", rejected!.Value.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://evil.example.test")]
    public async Task Identity_live_rejects_missing_or_untrusted_origin(string? origin)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/me/live/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // PRD-02/60-TC-05/08/09: actual websocket delivery and fresh-session revocation.
    [Fact]
    public async Task Identity_live_delivers_own_profile_changes_and_stops_after_logout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var socket = await LiveSocket(app, cookie, "/me/live");
        await SendFrame(socket, new { type = 4, invocationId = "identity", target = "Watch", arguments = new string?[] { null } });
        var initial = await StreamItem(socket);
        Assert.Equal("ACTIVE", initial.GetProperty("profile").GetProperty("status").GetString());
        Assert.Empty(initial.GetProperty("events").EnumerateArray());
        Assert.Equal(1, initial.GetProperty("cursor").GetInt64());
        using var changed = await Mutate(client, HttpMethod.Patch, "/me", new { displayName = "Live account", version = 1 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var page = await StreamItem(socket);
        Assert.Equal("Live account", page.GetProperty("profile").GetProperty("displayName").GetString());
        Assert.Equal("USER_PROFILE_UPDATED", Assert.Single(page.GetProperty("events").EnumerateArray()).GetProperty("eventType").GetString());
        using var logout = await Mutate(client, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var closed = false;
        try
        {
            while (!closed)
            {
                var message = await Frame(socket, deadline.Token);
                closed = message is null || message.Value.GetProperty("type").GetInt32() is 3 or 7;
                if (message is not null && !closed) Assert.NotEqual(2, message.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { closed = true; }
        Assert.True(closed);
    }
}
