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
    private static async Task<(Guid Organization, Guid Board, Guid List, Guid Owner, Guid Recipient)> NotificationFixture(
        ApiFactory app, HttpClient owner, HttpClient recipient, CancellationToken ct)
    {
        await RegisterAndLogin(owner); await RegisterAndLogin(recipient);
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
        return (org, board, list, actor, user);
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
            Assert.Equal(new[] { "actorId", "boardId", "createdAt", "entityId", "entityLink", "entityType", "id", "readAt", "recipientId", "type" },
                item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        });
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(path + $"?recipientId={f.Recipient}", ct)).GetProperty("items").EnumerateArray());
        var first = items[0].GetProperty("id").GetGuid(); var second = items[1].GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString();
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
        var bulkKey = Guid.NewGuid().ToString();
        using var bulk = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { first, second } }, bulkKey);
        Assert.Equal(HttpStatusCode.OK, bulk.StatusCode); var bulkReceipt = await bulk.Content.ReadAsStringAsync(ct);
        Assert.Equal(readAt, (await bulk.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("id").GetGuid() == first).GetProperty("readAt").GetDateTimeOffset());
        using var bulkReplay = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { second, first } }, bulkKey);
        Assert.Equal(bulkReceipt, await bulkReplay.Content.ReadAsStringAsync(ct));
        using var reused = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = new[] { second } }, bulkKey);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
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
