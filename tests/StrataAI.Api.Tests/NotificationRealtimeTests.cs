using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Notification_live_skips_existing_backlog_and_delivers_created_and_first_read_transitions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var oldCard = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Existing private notification", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await work.SetCardMemberAsync(oldCard.Id, f.Recipient, f.Owner, true, 1, "live-test", ct)).Succeeded);
        using var socket = await LiveSocket(app, f.RecipientCookie, "/notifications/live");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { f.Organization.ToString(), null } });
        var initial = await StreamItem(socket);
        Assert.Equal("1", initial.GetProperty("cursor").GetString());
        Assert.Empty(initial.GetProperty("events").EnumerateArray());
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "New private notification", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await work.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, 1, "live-test", ct)).Succeeded);
        var page = await StreamItem(socket);
        var created = Assert.Single(page.GetProperty("events").EnumerateArray());
        Assert.Equal("2", page.GetProperty("cursor").GetString());
        Assert.Equal("NOTIFICATION_CREATED", created.GetProperty("eventType").GetString());
        Assert.Equal(f.Recipient, created.GetProperty("recipientId").GetGuid());
        Assert.Equal(f.Owner, created.GetProperty("actorId").GetGuid());
        Assert.Empty(created.GetProperty("metadata").EnumerateObject());
        Assert.DoesNotContain("New private notification", page.GetRawText());
        var id = created.GetProperty("entityId").GetGuid();
        using var read = await Mutate(recipient, HttpMethod.Post, $"/organizations/{f.Organization}/notifications/read", new { ids = new[] { id } });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var receipt = await read.Content.ReadFromJsonAsync<JsonElement>(ct);
        var readPage = await StreamItem(socket);
        var change = Assert.Single(readPage.GetProperty("events").EnumerateArray());
        Assert.Equal("3", readPage.GetProperty("cursor").GetString());
        Assert.Equal("NOTIFICATION_READ", change.GetProperty("eventType").GetString());
        Assert.Equal(id, change.GetProperty("entityId").GetGuid());
        Assert.Equal(f.Recipient, change.GetProperty("actorId").GetGuid());
        Assert.Equal(receipt.GetProperty("items")[0].GetProperty("readAt").GetDateTimeOffset(), change.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Notification_live_initializes_at_head_resets_future_cursor_and_releases_cancelled_subscription()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var created = await Mutate(client, HttpMethod.Post, "/organizations", new { name = "Private live notifications" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var socket = await LiveSocket(app, cookie, "/notifications/live");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { org.ToString(), null } });
        var initial = await StreamItem(socket);
        Assert.Equal(org, initial.GetProperty("organizationId").GetGuid());
        Assert.Equal("0", initial.GetProperty("cursor").GetString());
        Assert.Empty(initial.GetProperty("events").EnumerateArray());
        Assert.False(initial.GetProperty("resetRequired").GetBoolean());
        await SendFrame(socket, new { type = 4, invocationId = "duplicate", target = "Watch", arguments = new[] { org.ToString(), "0" } });
        Assert.Contains("subscription_limit", (await Frame(socket, ct))!.Value.GetProperty("error").GetString());
        await SendFrame(socket, new { type = 5, invocationId = "watch" });
        Assert.Equal(3, (await Frame(socket, ct))!.Value.GetProperty("type").GetInt32());
        await SendFrame(socket, new { type = 4, invocationId = "reset", target = "Watch", arguments = new[] { org.ToString(), "9223372036854775807" } });
        var reset = await StreamItem(socket);
        Assert.True(reset.GetProperty("resetRequired").GetBoolean());
        Assert.Equal("0", reset.GetProperty("cursor").GetString());
        Assert.Empty(reset.GetProperty("events").EnumerateArray());
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

    [Theory]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("9223372036854775808")]
    public async Task Notification_live_rejects_invalid_cursors(string cursor)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var socket = await LiveSocket(app, cookie, "/notifications/live");
        await SendFrame(socket, new { type = 4, invocationId = "invalid", target = "Watch", arguments = new[] { Guid.NewGuid().ToString(), cursor } });
        Assert.Contains("invalid_notification_cursor", (await Frame(socket, TestContext.Current.CancellationToken))!.Value.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://evil.example.test")]
    public async Task Notification_live_rejects_missing_or_untrusted_origin(string? origin)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/notifications/live/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
