using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PRD_07_whole_List_HTTP_movement_rebinds_search_and_withdraws_old_navigation_receipt(bool eligible)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var destination = await ListMoveDestination(owner, f.Organization, ct);
        if (eligible)
        {
            using var granted = await Mutate(owner, HttpMethod.Patch, $"/boards/{destination}/members/{f.Recipient}", new { role = "MEMBER" });
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        }
        var keyword = "listmove" + Guid.NewGuid().ToString("N");
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = keyword, description = "Stable private moved result" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var path = $"/search?q={keyword}&scope=active&match=ALL";
        var initial = await member.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.Equal(card, Assert.Single(initial.GetProperty("items").EnumerateArray()).GetProperty("card").GetProperty("id").GetGuid());
        var cursor = initial.GetProperty("nextCursor").GetString(); Assert.False(string.IsNullOrEmpty(cursor));
        async Task<HttpResponseMessage> Observe(Guid board, long version, string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/navigation/observations?kind=card&organizationId={f.Organization}&boardId={board}&cardId={card}&version={version}");
            request.Headers.Add("X-StrataAI-Request", "1"); request.Headers.Add("X-StrataAI-Expected-Actor", f.Recipient.ToString("D"));
            request.Headers.Add("Idempotency-Key", key); return await member.SendAsync(request, ct);
        }
        var originalKey = Guid.NewGuid().ToString();
        using var opened = await Observe(f.Board, 1, originalKey); Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        using var moved = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move",
            new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = 1 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var current = await member.GetFromJsonAsync<JsonElement>(path, ct);
        var items = current.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(eligible ? 1 : 0, items.Length);
        if (eligible)
        {
            Assert.Equal(card, items[0].GetProperty("card").GetProperty("id").GetGuid());
            Assert.Equal(destination, items[0].GetProperty("card").GetProperty("boardId").GetGuid());
            Assert.Equal(f.List, items[0].GetProperty("card").GetProperty("listId").GetGuid());
            Assert.Equal("Move destination", items[0].GetProperty("boardName").GetString());
        }
        var continued = await member.GetFromJsonAsync<JsonElement>($"{path}&after={Uri.EscapeDataString(cursor!)}", ct);
        var continuedItems = continued.GetProperty("items").EnumerateArray().ToArray();
        if (!eligible) Assert.Empty(continuedItems);
        Assert.All(continuedItems, item => Assert.Equal(destination, item.GetProperty("card").GetProperty("boardId").GetGuid()));
        using var oldReceipt = await Observe(f.Board, 1, originalKey);
        Assert.Equal(HttpStatusCode.NotFound, oldReceipt.StatusCode);
        var denied = await oldReceipt.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("eventId", denied); Assert.DoesNotContain(keyword, denied);
        var destinationKey = Guid.NewGuid().ToString();
        using var openedDestination = await Observe(destination, 2, destinationKey);
        Assert.Equal(eligible ? HttpStatusCode.OK : HttpStatusCode.NotFound, openedDestination.StatusCode);
        if (eligible)
        {
            var acknowledged = await openedDestination.Content.ReadAsStringAsync(ct);
            using var replay = await Observe(destination, 2, destinationKey);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(acknowledged, await replay.Content.ReadAsStringAsync(ct));
            using var revoke = await Mutate(owner, HttpMethod.Delete, $"/boards/{destination}/members/{f.Recipient}", new { });
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            using var withdrawn = await Observe(destination, 2, destinationKey); Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
            Assert.DoesNotContain("eventId", await withdrawn.Content.ReadAsStringAsync(ct));
            var hidden = await member.GetFromJsonAsync<JsonElement>(path, ct); Assert.Empty(hidden.GetProperty("items").EnumerateArray());
        }
        Assert.Equal(2, (await app.Services.GetRequiredService<IWorkManagementStore>().FindCardAsync(card, ct))!.Version);
    }
}
