using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("http://evil.example.test")]
    [InlineData("http://localhost:81")]
    [InlineData("http://localhost/path")]
    public async Task Live_negotiate_rejects_missing_or_wrong_origin_without_disclosure(string? origin)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/boards/live/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("realtime_origin_denied", (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_stream_delivers_changes_then_closes_after_session_or_membership_revocation(bool membership)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        var actor = (await client.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var administrator = app.CreateClient();
        if (membership) await RegisterAndLogin(administrator);
        using var organization = await Mutate(membership ? administrator : client, HttpMethod.Post, "/organizations", new { name = "Live Organization" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        if (membership) await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var created = await Mutate(client, HttpMethod.Post, "/boards", new { organizationId = org, name = "Protected live board" });
        var board = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var socket = await LiveSocket(app, cookie);
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new[] { board.ToString(), "0" } });
        var first = await StreamItem(socket);
        Assert.Equal("1", first.GetProperty("cursor").GetString());
        using var listed = await Mutate(client, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Sensitive list name" });
        var changed = await StreamItem(socket);
        Assert.Equal("2", changed.GetProperty("cursor").GetString());
        Assert.DoesNotContain("Sensitive list name", changed.GetRawText());
        if (membership)
        {
            var organizations = app.Services.GetRequiredService<IOrganizationStore>();
            Assert.Equal(OrganizationRemoveMemberResult.Removed, await organizations.RemoveMemberAsync(org, actor, DateTimeOffset.UtcNow, ct));
        }
        else
        {
            using var logout = await Mutate(client, HttpMethod.Post, "/auth/logout", new { });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var closed = false;
        try
        {
            while (!closed)
            {
                var message = await Frame(socket, deadline.Token);
                closed = message is null || message.Value.TryGetProperty("type", out var type) && type.GetInt32() is 3 or 7;
                if (message is not null && !closed) Assert.NotEqual(2, message.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { closed = true; }
        Assert.True(closed);
    }

    [Fact]
    public async Task Live_stream_denies_private_board_to_anonymous_socket()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient(); await RegisterAndLogin(client);
        using var organization = await Mutate(client, HttpMethod.Post, "/organizations", new { name = "Denied live" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var created = await Mutate(client, HttpMethod.Post, "/boards", new { organizationId = org, name = "Hidden socket board" });
        var board = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var socket = await LiveSocket(app, null);
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new[] { board.ToString(), "0" } });
        try
        {
            var result = await Frame(socket, ct);
            Assert.True(result is null || result.Value.GetProperty("type").GetInt32() is 3 or 7);
            if (result is not null) Assert.DoesNotContain("Hidden socket board", result.Value.GetRawText());
        }
        catch (WebSocketException) { }
    }

    [Fact]
    public async Task Live_connection_limits_subscriptions_and_releases_scope_on_stream_cancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var organization = await Mutate(client, HttpMethod.Post, "/organizations", new { name = "Cancellation" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var created = await Mutate(client, HttpMethod.Post, "/boards", new { organizationId = org, name = "One live subscription" });
        var board = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var socket = await LiveSocket(app, cookie);
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new[] { board.ToString(), "0" } });
        await StreamItem(socket);
        await SendFrame(socket, new { type = 4, invocationId = "duplicate", target = "Watch", arguments = new[] { board.ToString(), "0" } });
        var rejected = await Frame(socket, ct);
        Assert.Equal(3, rejected!.Value.GetProperty("type").GetInt32());
        Assert.Contains("subscription_limit", rejected.Value.GetProperty("error").GetString());
        await SendFrame(socket, new { type = 5, invocationId = "watch" });
        var completed = await Frame(socket, ct);
        Assert.Equal(3, completed!.Value.GetProperty("type").GetInt32());
        await SendFrame(socket, new { type = 4, invocationId = "replacement", target = "Watch", arguments = new[] { board.ToString(), "1" } });
        var replacement = await StreamItem(socket);
        Assert.Equal("1", replacement.GetProperty("cursor").GetString());
        Assert.Empty(replacement.GetProperty("events").EnumerateArray());
    }

    private static async Task<WebSocket> LiveSocket(ApiFactory app, string? cookie, string path = "/boards/live")
    {
        var client = app.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        { request.Headers.Origin = "http://localhost"; if (cookie is not null) request.Headers.Cookie = cookie; };
        var socket = await client.ConnectAsync(new Uri("ws://localhost" + path), TestContext.Current.CancellationToken);
        await SendFrame(socket, new { protocol = "json", version = 1 });
        var handshake = await Frame(socket, TestContext.Current.CancellationToken);
        Assert.NotNull(handshake); Assert.Equal("{}", handshake.Value.GetRawText());
        return socket;
    }
    private static Task SendFrame(WebSocket socket, object message) => socket.SendAsync(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + '\u001e'), WebSocketMessageType.Text, true,
        TestContext.Current.CancellationToken);
    private static async Task<JsonElement> StreamItem(WebSocket socket)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        while (true)
        {
            var frame = await Frame(socket, deadline.Token);
            Assert.NotNull(frame);
            if (frame.Value.TryGetProperty("type", out var type) && type.GetInt32() == 2) return frame.Value.GetProperty("item");
        }
    }
    private static async Task<JsonElement?> Frame(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[131072];
        var received = await socket.ReceiveAsync(buffer, ct);
        if (received.MessageType == WebSocketMessageType.Close) return null;
        Assert.True(received.EndOfMessage);
        var text = Encoding.UTF8.GetString(buffer, 0, received.Count).TrimEnd('\u001e');
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
