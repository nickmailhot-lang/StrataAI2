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
    public async Task Private_notification_sync_is_bounded_freshly_admitted_and_recovers_hidden_windows_without_disclosure()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>(); var store = app.Services.GetRequiredService<IWorkManagementStore>();
        for (var index = 0; index < 52; index++)
        {
            var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Private journal content", null, null, DateTimeOffset.UtcNow, ct);
            Assert.True((await work.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, 1, "private-sync-test", ct)).Succeeded);
        }
        var path = $"/organizations/{f.Organization}/notifications/sync";
        using var firstReply = await recipient.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, firstReply.StatusCode);
        Assert.True(firstReply.Headers.CacheControl!.NoStore); Assert.True(firstReply.Headers.CacheControl.Private);
        var first = await firstReply.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(f.Recipient, first.GetProperty("recipientId").GetGuid()); Assert.Equal("50", first.GetProperty("cursor").GetString());
        Assert.True(first.GetProperty("hasMore").GetBoolean()); Assert.Equal(50, first.GetProperty("events").GetArrayLength());
        var second = await recipient.GetFromJsonAsync<JsonElement>(path + "?after=50", ct);
        Assert.Equal(2, second.GetProperty("events").GetArrayLength()); Assert.Equal("52", second.GetProperty("cursor").GetString());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal(52, first.GetProperty("events").EnumerateArray().Concat(second.GetProperty("events").EnumerateArray())
            .Select(e => e.GetProperty("eventId").GetGuid()).Distinct().Count());
        var own = await owner.GetFromJsonAsync<JsonElement>(path, ct); Assert.Equal(0, own.GetProperty("events").GetArrayLength());
        using var unauthenticated = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        foreach (var cursor in new[] { "-1", "", "9223372036854775808", "private-material" })
        {
            using var invalid = await recipient.GetAsync(path + "?after=" + cursor, ct); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var revoke = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        var hidden = await recipient.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.Equal(0, hidden.GetProperty("events").GetArrayLength()); Assert.Equal("50", hidden.GetProperty("cursor").GetString());
        Assert.True(hidden.GetProperty("hasMore").GetBoolean());
        var hiddenLast = await recipient.GetFromJsonAsync<JsonElement>(path + "?after=50", ct);
        Assert.Equal(0, hiddenLast.GetProperty("events").GetArrayLength()); Assert.Equal("52", hiddenLast.GetProperty("cursor").GetString());
        Assert.False(hiddenLast.GetProperty("hasMore").GetBoolean());
        Assert.DoesNotContain("Private journal content", hidden.GetRawText());
        Assert.DoesNotContain(first.GetProperty("events")[0].GetProperty("entityId").GetGuid().ToString(), hidden.GetRawText());
        using var restore = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var recovered = await recipient.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.Equal(first.GetProperty("events").GetRawText(), recovered.GetProperty("events").GetRawText());
    }

    [Fact]
    public async Task Demo_notification_journal_records_first_read_once_across_command_replay_and_new_keys()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Private notification source", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await app.Services.GetRequiredService<IWorkManagementService>()
            .SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, 1, "journal-test", ct)).Succeeded);
        var journal = app.Services.GetRequiredService<INotificationRealtimeStore>();
        var created = Assert.Single(await journal.ListRecipientEventsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        Assert.Equal("NOTIFICATION_CREATED", created.EventType); Assert.Equal(f.Owner, created.ActorId);
        Assert.Empty(await journal.ListRecipientEventsAsync(f.Organization, f.Owner, cancellationToken: ct));
        var path = $"/organizations/{f.Organization}/notifications/read"; var key = Guid.NewGuid().ToString();
        var selection = new { ids = new[] { created.EntityId } };
        using var read = await Mutate(recipient, HttpMethod.Post, path, selection, key);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); var receipt = await read.Content.ReadAsStringAsync(ct);
        var readEvent = Assert.Single(await journal.ListRecipientEventsAsync(f.Organization, f.Recipient, 1, ct));
        Assert.Equal("NOTIFICATION_READ", readEvent.EventType); Assert.Equal("2", readEvent.Sequence);
        using var acknowledged = JsonDocument.Parse(receipt);
        Assert.Equal(acknowledged.RootElement.GetProperty("items")[0].GetProperty("readAt").GetDateTimeOffset(), readEvent.CreatedAt);
        Assert.Equal(f.Recipient, readEvent.ActorId); Assert.Equal(created.EntityId, readEvent.EntityId); Assert.Empty(readEvent.Metadata);
        using var replay = await Mutate(recipient, HttpMethod.Post, path, selection, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var repeated = await Mutate(recipient, HttpMethod.Post, path, selection, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode); Assert.Equal(receipt, await repeated.Content.ReadAsStringAsync(ct));
        Assert.Equal(readEvent, Assert.Single(await journal.ListRecipientEventsAsync(f.Organization, f.Recipient, 1, ct)));
        Assert.Empty(await journal.ListRecipientEventsAsync(f.Organization, f.Recipient, 2, ct));
        using var denied = await Mutate(owner, HttpMethod.Post, path, selection);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(2, (await journal.ListRecipientEventsAsync(f.Organization, f.Recipient, cancellationToken: ct)).Count);
    }

    private static async Task<(Guid Organization, Guid Board, Guid List, Guid Owner, Guid Recipient, string RecipientCookie)> NotificationFixture(
        ApiFactory app, HttpClient owner, HttpClient recipient, CancellationToken ct)
    {
        await RegisterAndLogin(owner); var recipientCookie = await RegisterAndLogin(recipient);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var user = (await recipient.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var createdOrg = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Notification Organization" });
        var org = (await createdOrg.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, user, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var createdBoard = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Private notification Board" });
        var board = (await createdBoard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/members/{user}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var createdList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Notification List" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        return (org, board, list, actor, user, recipientCookie);
    }

    [Fact]
    public async Task Notification_inbox_is_recipient_scoped_and_read_actions_are_atomic_repeatable_and_freshly_authorized()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var work = app.Services.GetRequiredService<IWorkManagementService>();
        for (var index = 0; index < 2; index++)
        {
            var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Protected notification Card", null, null, DateTimeOffset.UtcNow, ct);
            Assert.True((await work.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, 1, "notification-test", ct)).Succeeded);
        }
        var path = $"/organizations/{f.Organization}/notifications";
        var page = await recipient.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.Equal(f.Organization, page.GetProperty("organizationId").GetGuid()); Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        var items = page.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(2, items.Length);
        Assert.All(items, item =>
        {
            Assert.Equal(f.Owner, item.GetProperty("actorId").GetGuid()); Assert.Equal(f.Recipient, item.GetProperty("recipientId").GetGuid());
            Assert.Equal("CARD_ASSIGNED", item.GetProperty("type").GetString()); Assert.Equal("Card", item.GetProperty("entityType").GetString());
            Assert.Equal(f.Board, item.GetProperty("boardId").GetGuid());
            Assert.Equal($"/app/{f.Organization}/boards/{f.Board}/cards/{item.GetProperty("entityId").GetGuid()}", item.GetProperty("entityLink").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("readAt").ValueKind);
            Assert.Equal(item.GetProperty("createdAt").GetDateTimeOffset(), item.GetProperty("updatedAt").GetDateTimeOffset());
            Assert.Equal(new[] { "actorId", "boardId", "createdAt", "entityId", "entityLink", "entityType", "id", "readAt", "recipientId", "type", "updatedAt" },
                item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        });
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(path + $"?recipientId={f.Recipient}", ct)).GetProperty("items").EnumerateArray());
        var first = items[0].GetProperty("id").GetGuid(); var second = items[1].GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString();
        using var invalidKey = await Mutate(recipient, HttpMethod.Post, $"{path}/{first}/read", new { }, "invalid-retry-key");
        Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await invalidKey.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var invalidBulkKey = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { first, second } }, "invalid-retry-key");
        Assert.Equal(HttpStatusCode.BadRequest, invalidBulkKey.StatusCode);
        using var read = await Mutate(recipient, HttpMethod.Post, $"{path}/{first}/read", new { }, key);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); var receipt = await read.Content.ReadAsStringAsync(ct);
        var readAt = (await read.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items")[0].GetProperty("readAt").GetDateTimeOffset();
        using var replay = await Mutate(recipient, HttpMethod.Post, $"{path}/{first}/read", new { }, key);
        Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var naturalReplay = await Mutate(recipient, HttpMethod.Post, $"{path}/{first}/read", new { });
        Assert.Equal(receipt, await naturalReplay.Content.ReadAsStringAsync(ct));
        using var deniedBulk = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { second, Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.NotFound, deniedBulk.StatusCode);
        var unchanged = (await recipient.GetFromJsonAsync<JsonElement>(path, ct)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(JsonValueKind.Null, unchanged.Single(n => n.GetProperty("id").GetGuid() == second).GetProperty("readAt").ValueKind);
        Assert.Equal(readAt, unchanged.Single(n => n.GetProperty("id").GetGuid() == first).GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(items.Single(n => n.GetProperty("id").GetGuid() == second).GetProperty("createdAt").GetDateTimeOffset(),
            unchanged.Single(n => n.GetProperty("id").GetGuid() == second).GetProperty("updatedAt").GetDateTimeOffset());
        var bulkKey = Guid.NewGuid().ToString();
        using var bulk = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { first, second } }, bulkKey);
        Assert.Equal(HttpStatusCode.OK, bulk.StatusCode); var bulkReceipt = await bulk.Content.ReadAsStringAsync(ct);
        Assert.Equal(readAt, (await bulk.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("id").GetGuid() == first).GetProperty("readAt").GetDateTimeOffset());
        using var bulkReplay = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { second, first } }, bulkKey);
        Assert.Equal(bulkReceipt, await bulkReplay.Content.ReadAsStringAsync(ct));
        using var reused = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { second } }, bulkKey);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var afterBulk = (await recipient.GetFromJsonAsync<JsonElement>(path, ct)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(readAt, afterBulk.Single(n => n.GetProperty("id").GetGuid() == first).GetProperty("updatedAt").GetDateTimeOffset());
        Assert.All(afterBulk, item => Assert.Equal(item.GetProperty("readAt").GetDateTimeOffset(), item.GetProperty("updatedAt").GetDateTimeOffset()));
        foreach (var selection in new[] { Array.Empty<Guid>(), new[] { first, first }, new[] { Guid.Empty }, Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray() })
        {
            using var invalid = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = selection });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var cursor = await recipient.GetAsync(path + "?after=bad", ct); Assert.Equal(HttpStatusCode.BadRequest, cursor.StatusCode);
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var hidden = await outsider.GetAsync(path + "?after=bad", ct); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var wrongRecipient = await Mutate(owner, HttpMethod.Post, $"{path}/{first}/read", new { }); Assert.Equal(HttpStatusCode.NotFound, wrongRecipient.StatusCode);
        using var anonymous = app.CreateClient(); using var denied = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.True((await work.RemoveBoardMemberAsync(f.Board, f.Owner, f.Recipient, "notification-test", ct)).Succeeded);
        Assert.Empty((await recipient.GetFromJsonAsync<JsonElement>(path, ct)).GetProperty("items").EnumerateArray());
        using var revokedReplay = await Mutate(recipient, HttpMethod.Post, $"{path}/{first}/read", new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, revokedReplay.StatusCode);
        Assert.DoesNotContain("Protected", await revokedReplay.Content.ReadAsStringAsync(ct));
        Assert.True((await work.SetBoardMemberAsync(f.Board, f.Owner, f.Recipient, BoardRole.Member, "notification-test", ct)).Succeeded);
        Assert.Equal(2, (await recipient.GetFromJsonAsync<JsonElement>(path, ct)).GetProperty("items").GetArrayLength());
        using var archived = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/archive", new { version = 1 }); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Empty((await recipient.GetFromJsonAsync<JsonElement>(path, ct)).GetProperty("items").EnumerateArray());
        using var archivedReplay = await Mutate(recipient, HttpMethod.Post, $"{path}/{first}/read", new { }, key); Assert.Equal(HttpStatusCode.NotFound, archivedReplay.StatusCode);
    }

    [Fact]
    public async Task Notification_pages_order_newest_first_seek_50_plus_2_and_exclude_inaccessible_records_before_limit()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var work = app.Services.GetRequiredService<IWorkManagementService>();
        Guid last = default;
        for (var index = 0; index < 53; index++)
        {
            var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Paged notification Card", null, null, DateTimeOffset.UtcNow, ct);
            Assert.True((await work.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, 1, "notification-page", ct)).Succeeded); last = card.Id;
        }
        Assert.True((await work.SetCardLifecycleAsync(last, f.Owner, WorkItemLifecycleState.Archived, 2, "notification-page", ct)).Succeeded);
        var path = $"/organizations/{f.Organization}/notifications";
        var first = await recipient.GetFromJsonAsync<JsonElement>(path, ct); Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetString(); Assert.NotNull(cursor);
        var second = await recipient.GetFromJsonAsync<JsonElement>(path + "?after=" + Uri.EscapeDataString(cursor), ct);
        Assert.Equal(2, second.GetProperty("items").GetArrayLength()); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var items = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(52, items.Select(n => n.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.DoesNotContain(items, n => n.GetProperty("entityId").GetGuid() == last);
        var times = items.Select(n => n.GetProperty("createdAt").GetDateTimeOffset()).ToArray(); Assert.Equal(times.OrderDescending(), times);
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
        using var unavailable = await recipient.GetAsync(path + "?after=bad", ct); Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }
}
