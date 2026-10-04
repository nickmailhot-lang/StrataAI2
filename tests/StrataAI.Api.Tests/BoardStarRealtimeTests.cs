using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_04_Private_star_live_delivers_only_current_actor_transitions_and_stops_on_revocation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app,owner,member,ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        Assert.True((await work.SetStarAsync(f.Board,f.Owner,true,ct,0)).Succeeded);
        using var socket = await LiveSocket(app,f.RecipientCookie,"/boards/live/stars");
        await SendFrame(socket,new { type=4, invocationId="watch", target="Watch", arguments=new string?[] { f.Board.ToString(),null } });
        var initial = await StreamItem(socket);
        Assert.Equal("0",initial.GetProperty("cursor").GetString());
        Assert.Equal(f.Recipient,initial.GetProperty("userId").GetGuid());
        Assert.Empty(initial.GetProperty("events").EnumerateArray());
        Assert.True((await work.SetStarAsync(f.Board,f.Recipient,true,ct,0)).Succeeded);
        var first = await StreamItem(socket); var change = Assert.Single(first.GetProperty("events").EnumerateArray());
        Assert.Equal(f.Recipient,change.GetProperty("actorId").GetGuid()); Assert.Equal("BOARD_STARRED",change.GetProperty("eventType").GetString());
        Assert.Equal(1,change.GetProperty("version").GetInt64()); Assert.Empty(change.GetProperty("metadata").EnumerateObject());
        Assert.True((await work.SetStarAsync(f.Board,f.Owner,false,ct,1)).Succeeded);
        Assert.True((await work.SetStarAsync(f.Board,f.Recipient,false,ct,1)).Succeeded);
        var second = await StreamItem(socket);
        var next = Assert.Single(second.GetProperty("events").EnumerateArray());
        Assert.Equal(f.Recipient,next.GetProperty("actorId").GetGuid()); Assert.Equal(2,next.GetProperty("version").GetInt64());
        Assert.Equal("2",second.GetProperty("cursor").GetString());
        Assert.Equal(OrganizationRemoveMemberResult.Removed,await app.Services.GetRequiredService<IOrganizationStore>()
            .RemoveMemberAsync(f.Organization,f.Recipient,DateTimeOffset.UtcNow,ct));
        await AssertStarStreamClosed(socket,ct);
    }

    [Fact]
    public async Task PRD_04_Private_star_live_replays_after_future_cursor_reset_and_releases_cancelled_subscription()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app,owner,member,ct);
        Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().SetStarAsync(f.Board,f.Recipient,true,ct,0)).Succeeded);
        using var socket = await LiveSocket(app,f.RecipientCookie,"/boards/live/stars");
        await SendFrame(socket,new { type=4, invocationId="watch", target="Watch", arguments=new string?[] { f.Board.ToString(),null } });
        var initial = await StreamItem(socket);
        Assert.Equal("1",initial.GetProperty("cursor").GetString()); Assert.Empty(initial.GetProperty("events").EnumerateArray());
        await SendFrame(socket,new { type=4, invocationId="duplicate", target="Watch", arguments=new[] { f.Board.ToString(),"0" } });
        Assert.Contains("subscription_limit",(await Frame(socket,ct))!.Value.GetProperty("error").GetString());
        await SendFrame(socket,new { type=5, invocationId="watch" });
        Assert.Equal(3,(await Frame(socket,ct))!.Value.GetProperty("type").GetInt32());
        await SendFrame(socket,new { type=4, invocationId="reset", target="Watch", arguments=new[] { f.Board.ToString(),"9223372036854775807" } });
        var reset = await StreamItem(socket);
        Assert.True(reset.GetProperty("resetRequired").GetBoolean()); Assert.Equal("1",reset.GetProperty("cursor").GetString());
        Assert.Single(reset.GetProperty("events").EnumerateArray());
        using var logout = await Mutate(member,HttpMethod.Post,"/auth/logout",new { });
        Assert.Equal(HttpStatusCode.NoContent,logout.StatusCode);
        await AssertStarStreamClosed(socket,ct);
    }

    private static async Task AssertStarStreamClosed(WebSocket socket,CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var closed = false;
        try
        {
            while (!closed)
            {
                var frame = await Frame(socket,deadline.Token);
                closed = frame is null || frame.Value.GetProperty("type").GetInt32() is 3 or 7;
                if (frame is not null && !closed) Assert.NotEqual(2,frame.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { closed=true; }
        Assert.True(closed);
    }
}
