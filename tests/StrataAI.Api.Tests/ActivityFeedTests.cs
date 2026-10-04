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
    public async Task Activity_http_pages_visible_history_before_limit_and_binds_continuations_to_viewer_and_target()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var journal = app.Services.GetRequiredService<IWorkEventStore>();
        var transactions = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var watches = app.Services.GetRequiredService<IWatchSubscriptionStore>();
        // Fractional, non-UTC fixture verifies Demo projection matches PostgreSQL
        // precision without altering exact Work event retry identity.
        var at = DateTimeOffset.UtcNow.AddDays(1).ToOffset(TimeSpan.FromHours(2));
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Activity", null, null, at, ct);
        var published = new HashSet<Guid>();
        var seeded = await transactions.ExecuteReadAsync(f.Organization, null, "fixture_denied", () => Task.FromResult(true), async () =>
        {
            var hidden = await watches.SetAsync(f.Organization, f.Owner, "CARD", card.Id, true, 0, at, ct);
            Assert.NotNull(hidden);
            for (var i = 0; i < 65; i++)
            {
                var id = Guid.NewGuid(); published.Add(id);
                var change = new WorkEvent(id, f.Organization, f.Board, f.Owner, "CARD_UPDATED", "Card", card.Id, i + 1, "fixture", at);
                await journal.AppendAsync(change, ct); await journal.AppendAsync(change, ct);
            }
            for (var i = 0; i < 100; i++)
                await journal.AppendAsync(new(Guid.NewGuid(), f.Organization, f.Board, f.Recipient,
                    "WATCH_CREATED", "WatchSubscription", hidden.Id, 1, "private-fixture", at.AddDays(1)), ct);
            return WorkOperation<bool>.Success(true);
        }, ct);
        Assert.True(seeded.Succeeded);
        var path = $"/cards/{card.Id}/activity";
        using var response = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var first = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = first.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(50, items.Length);
        Assert.All(items, item => { Assert.Equal("Card", item.GetProperty("entityType").GetString());
            Assert.Equal(JsonValueKind.String, item.GetProperty("version").ValueKind);
            Assert.Empty(item.GetProperty("metadata").EnumerateObject());
            Assert.Equal(TimeSpan.Zero, item.GetProperty("createdAt").GetDateTimeOffset().Offset); });
        var cursor = first.GetProperty("nextCursor").GetString(); Assert.NotNull(cursor);
        var tail = await member.GetFromJsonAsync<JsonElement>($"{path}?after={Uri.EscapeDataString(cursor)}", ct);
        var rest = tail.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(15, rest.Length);
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);
        Assert.True(published.SetEquals(items.Concat(rest).Select(item => item.GetProperty("eventId").GetGuid())));
        using var wrongViewer = await owner.GetAsync($"{path}?after={Uri.EscapeDataString(cursor)}", ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongViewer.StatusCode);
        using var wrongTarget = await member.GetAsync($"/boards/{f.Board}/activity?after={Uri.EscapeDataString(cursor)}", ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongTarget.StatusCode);
        using var tampered = await member.GetAsync($"{path}?after=invalid", ct); Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        using var revoke = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        using var denied = await member.GetAsync($"{path}?after=invalid", ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }
}
