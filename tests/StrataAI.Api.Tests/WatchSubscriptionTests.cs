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
    public async Task Watches_are_personal_revisioned_and_replay_safe_for_all_three_entity_types()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Watchable Card", null, null, DateTimeOffset.UtcNow, ct);
        foreach (var (type, entity) in new[] { ("CARD", card.Id), ("LIST", f.List), ("BOARD", f.Board) })
        {
            var path = $"/watch/{type}/{entity}";
            var initial = await recipient.GetFromJsonAsync<WatchState>(path, ct);
            Assert.NotNull(initial); Assert.False(initial.Watching); Assert.Equal(0, initial.Version); Assert.Null(initial.SubscriptionId);
            Assert.True(initial.CanChange);
            using var invalidKey = await Mutate(recipient, HttpMethod.Put, path + "?version=0", new { }, "bad-key");
            Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
            var key = Guid.NewGuid().ToString();
            using var created = await Mutate(recipient, HttpMethod.Put, path + "?version=0", new { }, key);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var watched = await created.Content.ReadFromJsonAsync<WatchState>(ct); Assert.NotNull(watched);
            Assert.True(watched.Watching); Assert.True(watched.Changed); Assert.Equal(1, watched.Version);
            Assert.Equal(f.Recipient, watched.UserId); Assert.Equal(f.Organization, watched.OrganizationId); Assert.Equal(f.Board, watched.BoardId);
            Assert.NotNull(watched.SubscriptionId); Assert.Equal(watched.CreatedAt, watched.UpdatedAt);
            using var replay = await Mutate(recipient, HttpMethod.Put, path + "?version=0", new { }, key);
            Assert.Equal(await created.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
            using var noop = await Mutate(recipient, HttpMethod.Put, path + "?version=1", new { });
            var repeated = await noop.Content.ReadFromJsonAsync<WatchState>(ct); Assert.NotNull(repeated);
            Assert.False(repeated.Changed); Assert.Equal(watched.SubscriptionId, repeated.SubscriptionId); Assert.Equal(1, repeated.Version);
            var ownerState = await owner.GetFromJsonAsync<WatchState>(path + $"?userId={f.Recipient}", ct);
            Assert.NotNull(ownerState); Assert.False(ownerState.Watching); Assert.Equal(f.Owner, ownerState.UserId);
            using var stale = await Mutate(recipient, HttpMethod.Delete, path + "?version=0", new { }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var reused = await Mutate(recipient, HttpMethod.Delete, path + "?version=1", new { }, key); Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
            var deleteKey = Guid.NewGuid().ToString();
            using var removed = await Mutate(recipient, HttpMethod.Delete, path + "?version=1", new { }, deleteKey);
            var unwatched = await removed.Content.ReadFromJsonAsync<WatchState>(ct); Assert.NotNull(unwatched);
            Assert.False(unwatched.Watching); Assert.True(unwatched.Changed); Assert.Equal(2, unwatched.Version);
            Assert.Equal(watched.SubscriptionId, unwatched.SubscriptionId); Assert.Equal(watched.CreatedAt, unwatched.CreatedAt);
            using var deleteReplay = await Mutate(recipient, HttpMethod.Delete, path + "?version=1", new { }, deleteKey);
            Assert.Equal(await removed.Content.ReadAsStringAsync(ct), await deleteReplay.Content.ReadAsStringAsync(ct));
            using var restored = await Mutate(recipient, HttpMethod.Put, path + "?version=2", new { });
            var restoredState = await restored.Content.ReadFromJsonAsync<WatchState>(ct); Assert.NotNull(restoredState);
            Assert.True(restoredState.Watching); Assert.Equal(3, restoredState.Version); Assert.Equal(watched.SubscriptionId, restoredState.SubscriptionId);
        }
        Assert.Equal(1, (await store.FindCardAsync(card.Id, ct))!.Version);
        var events = await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var watchEvents = events.Events.Where(e => e.Event.EventType.StartsWith("WATCH_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(9, watchEvents.Length); Assert.All(watchEvents, e => Assert.Equal(f.Recipient, e.Event.ActorId));
        Assert.Equal(6, watchEvents.Count(e => e.Event.EventType == "WATCH_CREATED"));
        Assert.Equal(3, watchEvents.Count(e => e.Event.EventType == "WATCH_REMOVED"));
    }

    [Fact]
    public async Task Watch_receipts_reauthorize_recipient_entity_and_active_parents_and_never_disclose_to_an_outsider()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct); await RegisterAndLogin(outsider);
        var path = $"/watch/BOARD/{f.Board}"; var key = Guid.NewGuid().ToString();
        using var created = await Mutate(recipient, HttpMethod.Put, path + "?version=0", new { }, key); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path, ct)).StatusCode);
        using var invalidOutsider = await Mutate(outsider, HttpMethod.Put, path + "?version=invalid", new { }); Assert.Equal(HttpStatusCode.NotFound, invalidOutsider.StatusCode);
        using var invalid = await Mutate(recipient, HttpMethod.Put, path + "?version=invalid", new { }); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetAsync($"/watch/UNKNOWN/{f.Board}", ct)).StatusCode);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetAsync(path, ct)).StatusCode);
        using var deniedReplay = await Mutate(recipient, HttpMethod.Put, path + "?version=0", new { }, key); Assert.Equal(HttpStatusCode.NotFound, deniedReplay.StatusCode);
        using var restored = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "MEMBER" }); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var state = await recipient.GetFromJsonAsync<WatchState>(path, ct); Assert.NotNull(state); Assert.True(state.Watching); Assert.Equal(1, state.Version);
        using var archivedBoard = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 1 }); Assert.Equal(HttpStatusCode.OK, archivedBoard.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetAsync(path, ct)).StatusCode);
    }

    [Fact]
    public async Task Direct_Card_watches_survive_List_movement_and_are_hidden_by_an_archived_current_List()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Moving watched Card", null, null, DateTimeOffset.UtcNow, ct);
        var destination = await store.CreateListAsync(f.Board, Guid.NewGuid(), "Destination", null, DateTimeOffset.UtcNow, ct);
        var path = $"/watch/CARD/{card.Id}";
        using var watched = await Mutate(recipient, HttpMethod.Put, path + "?version=0", new { }); Assert.Equal(HttpStatusCode.OK, watched.StatusCode);
        var before = await watched.Content.ReadFromJsonAsync<WatchState>(ct); Assert.NotNull(before);
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/move", new { destinationListId = destination.Id, expectedVersion = 1 }); Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var after = await recipient.GetFromJsonAsync<WatchState>(path, ct); Assert.NotNull(after);
        Assert.Equal(before.SubscriptionId, after.SubscriptionId); Assert.Equal(before.Version, after.Version); Assert.True(after.Watching);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/lists/{destination.Id}/archive", new { version = 1 }); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetAsync(path, ct)).StatusCode);
        using var restored = await Mutate(owner, HttpMethod.Post, $"/lists/{destination.Id}/restore", new { version = 2 }); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var recovered = await recipient.GetFromJsonAsync<WatchState>(path, ct); Assert.NotNull(recovered); Assert.Equal(before.SubscriptionId, recovered.SubscriptionId); Assert.True(recovered.Watching);
    }
}
