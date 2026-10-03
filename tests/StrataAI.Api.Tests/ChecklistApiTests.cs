using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_13_Parent_archive_restore_and_delete_retain_children_without_disclosing_deleted_parents(bool listParent)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var children = app.Services.GetRequiredService<IChecklistStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Retained history", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/checklists"; var key = Guid.NewGuid().ToString();
        var createInput = new CreateChecklistInput("Retained checklist", 1);
        using var created = await Mutate(owner, HttpMethod.Post, path, createInput, key);
        var checklist = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!.Checklist;
        var itemPath = $"{path}/{checklist.Id}/items";
        using var added = await Mutate(owner, HttpMethod.Post, itemPath, new CreateChecklistItemInput("Retained item", 2, 1));
        var item = (await added.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!.Item;
        using var completion = await Mutate(member, HttpMethod.Patch, $"{itemPath}/{item.Id}", new UpdateChecklistItemInput(item.Text, true, 3, 2, 1));
        var completed = (await completion.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        var parentPath = listParent ? $"/lists/{f.List}" : $"/cards/{card.Id}";
        using var archived = await Mutate(owner, HttpMethod.Post, parentPath + "/archive", new { version = listParent ? 1 : 4 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        var readonlyPage = (await owner.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!;
        Assert.False(readonlyPage.CanEdit); Assert.Equal(completed.Item, Assert.Single(readonlyPage.Items)); Assert.Equal(100, readonlyPage.Summary.Percent);
        Assert.False((await owner.GetFromJsonAsync<ChecklistPage>(path, ct))!.CanEdit);
        using var unavailableRetry = await Mutate(owner, HttpMethod.Post, path, createInput, key);
        Assert.Equal(HttpStatusCode.NotFound, unavailableRetry.StatusCode);
        using var denied = await Mutate(owner, HttpMethod.Patch, $"{path}/{checklist.Id}", new RenameChecklistInput("Denied", listParent ? 4 : 5, 3));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(completed.Item, await children.FindItemAsync(f.Organization, checklist.Id, item.Id, ct));
        using var restored = await Mutate(owner, HttpMethod.Post, parentPath + "/restore", new { version = listParent ? 2 : 5 });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.True((await owner.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!.CanEdit);
        Assert.Equal(completed.Item, await children.FindItemAsync(f.Organization, checklist.Id, item.Id, ct));
        using var renamed = await Mutate(owner, HttpMethod.Patch, $"{path}/{checklist.Id}", new RenameChecklistInput("Restored checklist", listParent ? 4 : 6, 3));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var current = (await renamed.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        using var archivedAgain = await Mutate(owner, HttpMethod.Post, parentPath + "/archive", new { version = listParent ? 3 : 7 });
        Assert.Equal(HttpStatusCode.OK, archivedAgain.StatusCode);
        var deletePath = listParent ? parentPath + "?version=4&confirmed=true&containedCardCount=1" : parentPath + "?version=8&confirmed=true";
        using var deleted = await Mutate(owner, HttpMethod.Delete, deletePath, new { });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(itemPath, ct)).StatusCode);
        using var hiddenRetry = await Mutate(owner, HttpMethod.Post, path, createInput, key);
        Assert.Equal(HttpStatusCode.NotFound, hiddenRetry.StatusCode);
        Assert.Equal(current.Checklist, await children.FindAsync(f.Organization, card.Id, checklist.Id, ct, true));
        Assert.Equal(completed.Item, await children.FindItemAsync(f.Organization, checklist.Id, item.Id, ct, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_13_List_copy_preserves_all_active_checklist_content_with_independent_ids(bool crossBoard)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var children = app.Services.GetRequiredService<IChecklistStore>();
        var now = DateTimeOffset.UtcNow;
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Copy children", null, null, now, ct);
        var checklist = await children.CreateAsync(f.Organization, card.Id, "Ordered history", RankToken.After(null), now, ct);
        var originals = new List<ChecklistItemRecord>(); string? rank = null;
        for (var index = 0; index < 63; index++)
        {
            rank = RankToken.After(rank);
            originals.Add(await children.CreateItemAsync(f.Organization, checklist.Id, $"Item {index}", rank, now, ct));
        }
        originals[0] = (await children.UpdateItemAsync(f.Organization, checklist.Id, originals[0].Id, originals[0].Text, true, now, f.Owner, 1, now, ct))!;
        var deletedItem = await children.DeleteItemAsync(f.Organization, checklist.Id, originals[^1].Id, 1, now, ct);
        var removed = await children.CreateAsync(f.Organization, card.Id, "Deleted parent", RankToken.After(checklist.Rank), now, ct);
        await children.CreateItemAsync(f.Organization, removed.Id, "Hidden descendant", RankToken.After(null), now, ct);
        await children.DeleteAsync(f.Organization, card.Id, removed.Id, 1, f.Owner, "copy-fixture", now, ct);
        var destination = f.Board;
        if (crossBoard)
        {
            using var board = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Destination", visibility = "PRIVATE" });
            destination = (await board.Content.ReadFromJsonAsync<BoardRecord>(json, ct))!.Id;
        }
        var key = Guid.NewGuid().ToString(); var input = new { destinationBoardId = destination, name = "Checklist copy", version = 1 };
        using var copied = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/copy", input, key);
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        var list = (await copied.Content.ReadFromJsonAsync<BoardListRecord>(json, ct))!;
        var snapshot = (await owner.GetFromJsonAsync<BoardSnapshot>($"/boards/{destination}", json, ct))!;
        var copy = Assert.Single(Assert.Single(snapshot.Lists, entry => entry.List.Id == list.Id).Cards);
        var page = (await owner.GetFromJsonAsync<ChecklistPage>($"/cards/{copy.Id}/checklists", ct))!;
        var parent = Assert.Single(page.Items).Checklist;
        Assert.NotEqual(checklist.Id, parent.Id); Assert.Equal(checklist.Title, parent.Title); Assert.Equal(checklist.Rank, parent.Rank);
        Assert.Equal(1, parent.Version); Assert.Equal(copy.Id, parent.CardId); Assert.Equal(1, copy.Version);
        var itemPath = $"/cards/{copy.Id}/checklists/{parent.Id}/items";
        var first = (await owner.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!;
        Assert.Equal(62, first.Summary.Total); Assert.Equal(0, first.Summary.Completed); Assert.Equal(0, first.Summary.Percent); Assert.Equal(50, first.Items.Count);
        var second = (await owner.GetFromJsonAsync<ChecklistItemPage>($"{itemPath}?after={first.NextCursor}", ct))!;
        Assert.Equal(12, second.Items.Count); Assert.Null(second.NextCursor);
        var rows = first.Items.Concat(second.Items).ToArray();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index]; var original = originals[index];
            Assert.NotEqual(original.Id, row.Id); Assert.Equal(parent.Id, row.ChecklistId); Assert.Equal(1, row.Version);
            Assert.Equal(original.Text, row.Text); Assert.Equal(original.Rank, row.Rank); Assert.False(row.Completed);
            Assert.Null(row.CompletedAt); Assert.Null(row.CompletedBy); Assert.Null(row.DeletedAt);
            Assert.Equal(original, await children.FindItemAsync(f.Organization, checklist.Id, original.Id, ct));
        }
        Assert.Equal(deletedItem, await children.FindItemAsync(f.Organization, checklist.Id, originals[^1].Id, ct, true));
        using var replay = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/copy", input, key);
        Assert.Equal(await copied.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(62, (await owner.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!.Summary.Total);
    }

    [Fact]
    public async Task PRD_13_Public_checklist_reads_follow_current_visibility_and_never_grant_editing()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        using var outsider = app.CreateClient(); using var visitor = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Public Card", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/checklists";
        using var created = await Mutate(owner, HttpMethod.Post, path, new CreateChecklistInput("Public preparation", 1));
        var checklist = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!.Checklist;
        var itemsPath = $"{path}/{checklist.Id}/items";
        using var added = await Mutate(owner, HttpMethod.Post, itemsPath, new CreateChecklistItemInput("Visible item", 2, 1));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        foreach (var reader in new[] { visitor, outsider })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(path + "?after=malformed", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(itemsPath, ct)).StatusCode);
        }
        using var published = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = "PUBLIC", version = 1 });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        foreach (var reader in new[] { visitor, outsider })
        {
            var page = (await reader.GetFromJsonAsync<ChecklistPage>(path, ct))!;
            Assert.False(page.CanEdit); Assert.Equal(f.Organization, page.OrganizationId); Assert.Equal(card.Id, page.CardId);
            Assert.Equal("Public preparation", Assert.Single(page.Items).Checklist.Title); Assert.Equal(1, page.Items[0].Total);
            var items = (await reader.GetFromJsonAsync<ChecklistItemPage>(itemsPath, ct))!;
            Assert.False(items.CanEdit); Assert.Equal("Visible item", Assert.Single(items.Items).Text); Assert.Equal(1, items.Summary.Total);
            Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync(path + "?after=malformed", ct)).StatusCode);
        }
        using var anonymousWrite = await Mutate(visitor, HttpMethod.Post, path, new CreateChecklistInput("Denied", 3));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);
        using var outsiderWrite = await Mutate(outsider, HttpMethod.Post, path, new CreateChecklistInput("Denied", 3));
        Assert.Equal(HttpStatusCode.NotFound, outsiderWrite.StatusCode);
        using var hidden = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = "PRIVATE", version = 2 });
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        foreach (var reader in new[] { visitor, outsider })
        {
            using var denied = await reader.GetAsync(path, ct);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.DoesNotContain("Public preparation", await denied.Content.ReadAsStringAsync(ct));
            Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(itemsPath, ct)).StatusCode);
        }
        using var republished = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = "PUBLIC", version = 3 });
        Assert.Equal(HttpStatusCode.OK, republished.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 4 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(path, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(itemsPath, ct)).StatusCode);
        Assert.False((await owner.GetFromJsonAsync<ChecklistPage>(path, ct))!.CanEdit);
        Assert.False((await owner.GetFromJsonAsync<ChecklistItemPage>(itemsPath, ct))!.CanEdit);
        Assert.Equal(3, (await store.FindCardAsync(card.Id, ct))!.Version);
    }

    [Fact]
    public async Task PRD_13_Checklist_delete_cascades_beyond_a_page_preserves_old_tombstones_and_denies_child_replay()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var children = app.Services.GetRequiredService<IChecklistStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Cascade Card", null, null, DateTimeOffset.UtcNow, ct);
        var parentPath = $"/cards/{card.Id}/checklists"; var createKey = Guid.NewGuid().ToString();
        using var created = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput("History", 1), createKey);
        var checklist = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!.Checklist;
        var path = $"{parentPath}/{checklist.Id}"; var itemPath = $"{path}/items"; var items = new List<ChecklistItemRecord>();
        for (var index = 0; index < 63; index++)
        {
            using var added = await Mutate(owner, HttpMethod.Post, itemPath, new CreateChecklistItemInput($"Item {index}", index + 2, index + 1));
            Assert.Equal(HttpStatusCode.OK, added.StatusCode); items.Add((await added.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!.Item);
        }
        using var completed = await Mutate(member, HttpMethod.Patch, $"{itemPath}/{items[0].Id}", new UpdateChecklistItemInput(items[0].Text, true, 65, 64, 1));
        var done = (await completed.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        var leafKey = Guid.NewGuid().ToString(); var leafInput = new DeleteChecklistItemInput(true, 66, 65, 1);
        using var leafDeleted = await Mutate(owner, HttpMethod.Delete, $"{itemPath}/{items[^1].Id}", leafInput, leafKey);
        var oldTombstone = (await leafDeleted.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!.Item;
        var input = new DeleteChecklistInput(true, 67, 66); var key = Guid.NewGuid().ToString();
        using var denied = await Mutate(member, HttpMethod.Delete, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var unconfirmed = await Mutate(owner, HttpMethod.Delete, path, input with { Confirmed = false });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(62, (await owner.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!.Summary.Total);
        using var deleted = await Mutate(owner, HttpMethod.Delete, path, input, key);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var tombstone = (await deleted.Content.ReadFromJsonAsync<ChecklistDeletionChange>(ct))!;
        Assert.True(tombstone.Changed); Assert.Equal(68, tombstone.CardVersion); Assert.Equal(67, tombstone.Checklist.Version); Assert.Equal(62, tombstone.DeletedItems);
        Assert.NotNull(tombstone.Checklist.DeletedAt); Assert.Equal(checklist.Title, tombstone.Checklist.Title); Assert.Equal(checklist.Rank, tombstone.Checklist.Rank);
        Assert.Equal(oldTombstone, await children.FindItemAsync(f.Organization, checklist.Id, items[^1].Id, ct, true));
        foreach (var item in items.Take(62))
        {
            var retained = (await children.FindItemAsync(f.Organization, checklist.Id, item.Id, ct, true))!;
            Assert.NotNull(retained.DeletedAt); Assert.Equal(tombstone.Checklist.DeletedAt, retained.DeletedAt); Assert.Equal(item.Text, retained.Text);
            Assert.Null(await children.FindItemAsync(f.Organization, checklist.Id, item.Id, ct));
        }
        var history = (await children.FindItemAsync(f.Organization, checklist.Id, items[0].Id, ct, true))!;
        Assert.Equal(done.Item.CompletedAt, history.CompletedAt); Assert.Equal(done.Item.CompletedBy, history.CompletedBy); Assert.True(history.Completed);
        Assert.Empty((await member.GetFromJsonAsync<ChecklistPage>(parentPath, ct))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(itemPath, ct)).StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Delete, path, input, key);
        Assert.Equal(await deleted.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var noop = await Mutate(owner, HttpMethod.Delete, path, new DeleteChecklistInput(true, 68, 67));
        var unchanged = (await noop.Content.ReadFromJsonAsync<ChecklistDeletionChange>(ct))!;
        Assert.False(unchanged.Changed); Assert.Equal(0, unchanged.DeletedItems); Assert.Equal(tombstone.Checklist, unchanged.Checklist);
        using var hiddenCreate = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput("History", 1), createKey);
        Assert.Equal(HttpStatusCode.NotFound, hiddenCreate.StatusCode);
        using var hiddenLeafDelete = await Mutate(owner, HttpMethod.Delete, $"{itemPath}/{items[^1].Id}", leafInput, leafKey);
        Assert.Equal(HttpStatusCode.NotFound, hiddenLeafDelete.StatusCode);
    }

    [Fact]
    public async Task PRD_13_Item_deletion_requires_admin_confirmation_retains_history_and_supports_safe_retry()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Delete Card", null, null, DateTimeOffset.UtcNow, ct);
        var parentPath = $"/cards/{card.Id}/checklists";
        using var created = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput("Items", 1));
        var checklist = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!.Checklist;
        var itemPath = $"{parentPath}/{checklist.Id}/items";
        var createKey = Guid.NewGuid().ToString();
        using var added = await Mutate(member, HttpMethod.Post, itemPath, new CreateChecklistItemInput("Historical text", 2, 1), createKey);
        var first = (await added.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        var path = $"{itemPath}/{first.Item.Id}";
        using var completed = await Mutate(member, HttpMethod.Patch, path, new UpdateChecklistItemInput("Historical text", true, 3, 2, 1));
        var done = (await completed.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        var input = new DeleteChecklistItemInput(true, 4, 3, 2); var key = Guid.NewGuid().ToString();
        using var denied = await Mutate(member, HttpMethod.Delete, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var unconfirmed = await Mutate(owner, HttpMethod.Delete, path, input with { Confirmed = false });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(4, (await store.FindCardAsync(card.Id, ct))!.Version);
        using var deleted = await Mutate(owner, HttpMethod.Delete, path, input, key);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var tombstone = (await deleted.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.True(tombstone.Changed); Assert.Equal(5, tombstone.CardVersion); Assert.Equal(4, tombstone.Checklist.Version); Assert.Equal(3, tombstone.Item.Version);
        Assert.NotNull(tombstone.Item.DeletedAt); Assert.Equal(tombstone.Item.UpdatedAt, tombstone.Item.DeletedAt);
        Assert.Equal(done.Item.Text, tombstone.Item.Text); Assert.Equal(done.Item.Rank, tombstone.Item.Rank);
        Assert.Equal(done.Item.CompletedBy, tombstone.Item.CompletedBy); Assert.Equal(done.Item.CompletedAt, tombstone.Item.CompletedAt);
        var page = (await member.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!;
        Assert.Empty(page.Items); Assert.Equal(0, page.Summary.Total); Assert.Equal(0, page.Summary.Percent);
        using var replay = await Mutate(owner, HttpMethod.Delete, path, input, key);
        Assert.Equal(await deleted.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var noop = await Mutate(owner, HttpMethod.Delete, path, new DeleteChecklistItemInput(true, 5, 4, 3));
        var unchanged = (await noop.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.False(unchanged.Changed); Assert.Equal(tombstone.Item, unchanged.Item); Assert.Equal(5, unchanged.CardVersion);
        using var hiddenCreateReplay = await Mutate(member, HttpMethod.Post, itemPath, new CreateChecklistItemInput("Historical text", 2, 1), createKey);
        Assert.Equal(HttpStatusCode.NotFound, hiddenCreateReplay.StatusCode);
        using var cannotEdit = await Mutate(owner, HttpMethod.Patch, path, new UpdateChecklistItemInput("Resurrect", false, 5, 4, 3));
        Assert.Equal(HttpStatusCode.NotFound, cannotEdit.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = (await store.FindBoardAsync(f.Board, ct))!.Version });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var staleReplay = await Mutate(owner, HttpMethod.Delete, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, staleReplay.StatusCode);
    }

    [Fact]
    public async Task PRD_13_Checklist_and_item_positions_are_parent_scoped_CAS_retry_safe_and_noop_preserving()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Order Card", null, null, DateTimeOffset.UtcNow, ct);
        var parentPath = $"/cards/{card.Id}/checklists"; var children = new List<ChecklistRecord>();
        for (var index = 0; index < 3; index++)
        {
            using var created = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput($"Checklist {index}", index + 1));
            children.Add((await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!.Checklist);
        }
        var positionPath = $"{parentPath}/{children[2].Id}/position";
        var input = new ChecklistPositionInput(children[0].Id, 4, 1); var key = Guid.NewGuid().ToString();
        using var moved = await Mutate(member, HttpMethod.Patch, positionPath, input, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var change = (await moved.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        Assert.Equal(5, change.CardVersion); Assert.Equal(2, change.Checklist.Version); Assert.True(change.Changed);
        Assert.Equal(children[2].Title, change.Checklist.Title); Assert.Equal(children[2].CreatedAt, change.Checklist.CreatedAt);
        Assert.Equal(new[] { children[2].Id, children[0].Id, children[1].Id }, (await owner.GetFromJsonAsync<ChecklistPage>(parentPath, ct))!.Items.Select(item => item.Checklist.Id));
        using var replay = await Mutate(member, HttpMethod.Patch, positionPath, input, key);
        Assert.Equal(await moved.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var noop = await Mutate(owner, HttpMethod.Patch, positionPath, input with { CardVersion = 5, Version = 2 });
        var unchanged = (await noop.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        Assert.False(unchanged.Changed); Assert.Equal(change.Checklist, unchanged.Checklist); Assert.Equal(5, unchanged.CardVersion);
        using var append = await Mutate(owner, HttpMethod.Patch, positionPath, new ChecklistPositionInput(null, 5, 2));
        var appended = (await append.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        Assert.Equal(6, appended.CardVersion); Assert.Equal(3, appended.Checklist.Version);
        Assert.Equal(children.Select(child => child.Id), (await owner.GetFromJsonAsync<ChecklistPage>(parentPath, ct))!.Items.Select(item => item.Checklist.Id));
        foreach (var before in new Guid?[] { children[2].Id, Guid.NewGuid(), Guid.Empty })
        {
            using var invalid = await Mutate(owner, HttpMethod.Patch, positionPath, new ChecklistPositionInput(before, 6, 3));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var stale = await Mutate(owner, HttpMethod.Patch, positionPath, input);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var itemsPath = $"{parentPath}/{children[0].Id}/items"; var items = new List<ChecklistItemRecord>();
        for (var index = 0; index < 3; index++)
        {
            using var added = await Mutate(owner, HttpMethod.Post, itemsPath, new CreateChecklistItemInput($"Item {index}", index + 6, index + 1));
            items.Add((await added.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!.Item);
        }
        var itemPositionPath = $"{itemsPath}/{items[2].Id}/position";
        var itemInput = new ChecklistItemPositionInput(items[0].Id, 9, 4, 1); var itemKey = Guid.NewGuid().ToString();
        using var itemMoved = await Mutate(member, HttpMethod.Patch, itemPositionPath, itemInput, itemKey);
        Assert.Equal(HttpStatusCode.OK, itemMoved.StatusCode);
        var itemChange = (await itemMoved.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.Equal(10, itemChange.CardVersion); Assert.Equal(5, itemChange.Checklist.Version); Assert.Equal(2, itemChange.Item.Version);
        Assert.Equal(items[2].Text, itemChange.Item.Text); Assert.False(itemChange.Item.Completed); Assert.Null(itemChange.Item.CompletedAt);
        Assert.Equal(new[] { items[2].Id, items[0].Id, items[1].Id }, (await owner.GetFromJsonAsync<ChecklistItemPage>(itemsPath, ct))!.Items.Select(item => item.Id));
        using var itemNoop = await Mutate(owner, HttpMethod.Patch, itemPositionPath, itemInput with { CardVersion = 10, ChecklistVersion = 5, Version = 2 });
        var itemUnchanged = (await itemNoop.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.False(itemUnchanged.Changed); Assert.Equal(itemChange.Item, itemUnchanged.Item); Assert.Equal(itemChange.Checklist, itemUnchanged.Checklist);
        using var itemAppend = await Mutate(owner, HttpMethod.Patch, itemPositionPath, new ChecklistItemPositionInput(null, 10, 5, 2));
        Assert.Equal(HttpStatusCode.OK, itemAppend.StatusCode);
        var otherItemsPath = $"{parentPath}/{children[1].Id}/items";
        using var foreign = await Mutate(owner, HttpMethod.Post, otherItemsPath, new CreateChecklistItemInput("Foreign sibling", 11, 1));
        var foreignItem = (await foreign.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!.Item;
        using var invalidAnchor = await Mutate(owner, HttpMethod.Patch, itemPositionPath, new ChecklistItemPositionInput(foreignItem.Id, 12, 6, 3));
        Assert.Equal(HttpStatusCode.BadRequest, invalidAnchor.StatusCode);
        using var itemReplay = await Mutate(member, HttpMethod.Patch, itemPositionPath, itemInput, itemKey);
        Assert.Equal(await itemMoved.Content.ReadAsStringAsync(ct), await itemReplay.Content.ReadAsStringAsync(ct));
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var lostReplay = await Mutate(member, HttpMethod.Patch, itemPositionPath, itemInput, itemKey);
        Assert.Equal(HttpStatusCode.NotFound, lostReplay.StatusCode);
    }

    [Fact]
    public async Task PRD_13_Item_edit_completion_attribution_noops_and_due_completion_are_independent()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Completion Card", "Retained", null, DateTimeOffset.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates", new { dueAt = "2040-01-03T08:00:00Z", dueTimezone = "UTC", dueHasTime = true, dueComplete = true, version = 1 });
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        var parentPath = $"/cards/{card.Id}/checklists";
        using var created = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput("Items", 2));
        var checklist = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!.Checklist;
        var itemPath = $"{parentPath}/{checklist.Id}/items";
        using var added = await Mutate(member, HttpMethod.Post, itemPath, new CreateChecklistItemInput("Original", 3, 1));
        var first = (await added.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        var path = $"{itemPath}/{first.Item.Id}"; var input = new UpdateChecklistItemInput(" Original ", true, 4, 2, 1);
        using var denied = await Mutate(outsider, HttpMethod.Patch, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, path, input with { Text = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missingFlag = await Mutate(owner, HttpMethod.Patch, path, new { text = "Original", cardVersion = 4, checklistVersion = 2, version = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, missingFlag.StatusCode);
        var key = Guid.NewGuid().ToString();
        using var completed = await Mutate(member, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var done = (await completed.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.Equal(5, done.CardVersion); Assert.Equal(3, done.Checklist.Version); Assert.Equal(2, done.Item.Version);
        Assert.True(done.Item.Completed); Assert.Equal(f.Recipient, done.Item.CompletedBy); Assert.NotNull(done.Item.CompletedAt);
        var page = (await owner.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!;
        Assert.Equal(100, page.Summary.Percent);
        using var replay = await Mutate(member, HttpMethod.Patch, path, input, key);
        Assert.Equal(await completed.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        var next = input with { CardVersion = 5, ChecklistVersion = 3, Version = 2 };
        using var noop = await Mutate(owner, HttpMethod.Patch, path, next);
        var unchanged = (await noop.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.False(unchanged.Changed); Assert.Equal(done.Item, unchanged.Item); Assert.Equal(done.Checklist, unchanged.Checklist); Assert.Equal(5, unchanged.CardVersion);
        using var edited = await Mutate(owner, HttpMethod.Patch, path, next with { Text = "Edited" });
        var edit = (await edited.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.Equal(6, edit.CardVersion); Assert.Equal(4, edit.Checklist.Version); Assert.Equal(3, edit.Item.Version);
        Assert.Equal(done.Item.CompletedAt, edit.Item.CompletedAt); Assert.Equal(done.Item.CompletedBy, edit.Item.CompletedBy); Assert.Equal(first.Item.Rank, edit.Item.Rank);
        using var reopened = await Mutate(owner, HttpMethod.Patch, path, new UpdateChecklistItemInput("Edited", false, 6, 4, 3));
        var open = (await reopened.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.Equal(7, open.CardVersion); Assert.False(open.Item.Completed); Assert.Null(open.Item.CompletedBy); Assert.Null(open.Item.CompletedAt);
        Assert.Equal(0, (await member.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!.Summary.Percent);
        Assert.True((await store.FindCardAsync(card.Id, ct))!.DueComplete);
        foreach (var stale in new[] { new UpdateChecklistItemInput("Edited", true, 6, 5, 4), new("Edited", true, 7, 4, 4), new("Edited", true, 7, 5, 3) })
        {
            using var rejected = await Mutate(owner, HttpMethod.Patch, path, stale); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        using var otherChecklist = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput("Other", 7));
        var other = (await otherChecklist.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        using var wrong = await Mutate(owner, HttpMethod.Patch, $"{parentPath}/{other.Checklist.Id}/items/{first.Item.Id}", new UpdateChecklistItemInput("Wrong", true, 8, 1, 4));
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var lostReplay = await Mutate(member, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, lostReplay.StatusCode);
    }

    [Fact]
    public async Task PRD_13_Checklist_items_are_ordered_bounded_parent_scoped_and_retry_safe()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Items Card", "Retained", null, DateTimeOffset.UtcNow, ct);
        var parentPath = $"/cards/{card.Id}/checklists";
        using var created = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput("Items", 1));
        var first = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        var path = $"{parentPath}/{first.Checklist.Id}/items";
        var empty = (await member.GetFromJsonAsync<ChecklistItemPage>(path, ct))!;
        Assert.Empty(empty.Items); Assert.Equal(0, empty.Summary.Percent); Assert.True(empty.CanEdit);
        var originalCard = await store.FindCardAsync(card.Id, ct);
        foreach (var text in new string?[] { null, "", " ", "bad\0text", new('x', 2001) })
        {
            using var invalid = await Mutate(owner, HttpMethod.Post, path, new CreateChecklistItemInput(text, 2, 1));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(originalCard, await store.FindCardAsync(card.Id, ct));
        using var denied = await Mutate(outsider, HttpMethod.Post, path, new CreateChecklistItemInput("Protected", 2, 1));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var key = Guid.NewGuid().ToString(); var input = new CreateChecklistItemInput(" Item 00 ", 2, 1);
        using var added = await Mutate(member, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var change = (await added.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!;
        Assert.Equal(3, change.CardVersion); Assert.Equal(2, change.Checklist.Version); Assert.Equal(1, change.Item.Version);
        Assert.Equal("Item 00", change.Item.Text); Assert.False(change.Item.Completed); Assert.Null(change.Item.CompletedAt); Assert.Null(change.Item.CompletedBy);
        using var replay = await Mutate(member, HttpMethod.Post, path, input, key);
        Assert.Equal(await added.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Post, path, input);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        for (var index = 1; index < 63; index++)
        {
            using var next = await Mutate(owner, HttpMethod.Post, path, new CreateChecklistItemInput($"Item {index:00}", index + 2, index + 1));
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        }
        var page = (await member.GetFromJsonAsync<ChecklistItemPage>(path, ct))!;
        Assert.Equal(50, page.Items.Count); Assert.NotNull(page.NextCursor); Assert.Equal(63, page.Summary.Total); Assert.Equal(0, page.Summary.Completed);
        Assert.Equal(65, page.CardVersion); Assert.Equal(64, page.Summary.Checklist.Version);
        var tail = (await member.GetFromJsonAsync<ChecklistItemPage>($"{path}?after={page.NextCursor}", ct))!;
        Assert.Equal(13, tail.Items.Count); Assert.Null(tail.NextCursor); Assert.Equal(page.Summary, tail.Summary);
        var all = page.Items.Concat(tail.Items).ToArray(); Assert.Equal(63, all.Select(item => item.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 63).Select(index => $"Item {index:00}"), all.Select(item => item.Text));
        var summaries = (await owner.GetFromJsonAsync<ChecklistPage>(parentPath, ct))!;
        Assert.Equal(63, Assert.Single(summaries.Items).Total); Assert.Equal(0, summaries.Items[0].Percent);
        var another = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Other Card", null, null, DateTimeOffset.UtcNow, ct);
        using var wrongParent = await Mutate(owner, HttpMethod.Post, $"/cards/{another.Id}/checklists/{first.Checklist.Id}/items", new CreateChecklistItemInput("Wrong", 1, 64));
        Assert.Equal(HttpStatusCode.NotFound, wrongParent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{path}?after=malformed", ct)).StatusCode);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{path}?after={page.NextCursor}", ct)).StatusCode);
        using var lostReplay = await Mutate(member, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, lostReplay.StatusCode);
    }

    [Fact]
    public async Task PRD_13_Checklist_rename_checks_both_revisions_preserves_noops_and_rechecks_replays()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Checklist Card", "Retained", null, DateTimeOffset.UtcNow, ct);
        var parentPath = $"/cards/{card.Id}/checklists";
        using var created = await Mutate(owner, HttpMethod.Post, parentPath, new CreateChecklistInput("Original", 1));
        var first = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        var path = $"{parentPath}/{first.Checklist.Id}"; var input = new RenameChecklistInput(" Original ", 2, 1);
        using var denied = await Mutate(outsider, HttpMethod.Patch, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, path, input with { Title = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var before = await store.FindCardAsync(card.Id, ct);
        using var noop = await Mutate(member, HttpMethod.Patch, path, input);
        var unchanged = (await noop.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        Assert.False(unchanged.Changed); Assert.Equal(first.Checklist, unchanged.Checklist); Assert.Equal(before, await store.FindCardAsync(card.Id, ct));
        input = input with { Title = "Renamed" }; var key = Guid.NewGuid().ToString();
        using var renamed = await Mutate(member, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var change = (await renamed.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        Assert.True(change.Changed); Assert.Equal(3, change.CardVersion); Assert.Equal(2, change.Checklist.Version);
        Assert.Equal(first.Checklist.Rank, change.Checklist.Rank); Assert.Equal(first.Checklist.CreatedAt, change.Checklist.CreatedAt);
        using var replay = await Mutate(member, HttpMethod.Patch, path, input, key);
        Assert.Equal(await renamed.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        foreach (var staleInput in new[] { input with { CardVersion = 3 }, input with { Version = 2 }, input })
        {
            using var stale = await Mutate(owner, HttpMethod.Patch, path, staleInput);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }
        var another = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Other Card", null, null, DateTimeOffset.UtcNow, ct);
        using var wrongParent = await Mutate(owner, HttpMethod.Patch, $"/cards/{another.Id}/checklists/{first.Checklist.Id}", new RenameChecklistInput("Wrong parent", 1, 2));
        Assert.Equal(HttpStatusCode.NotFound, wrongParent.StatusCode);
        var responses = await Task.WhenAll(Mutate(owner, HttpMethod.Patch, path, new RenameChecklistInput("Concurrent A", 3, 2)),
            Mutate(member, HttpMethod.Patch, path, new RenameChecklistInput("Concurrent B", 3, 2)));
        try { Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in responses) response.Dispose(); }
        var current = (await owner.GetFromJsonAsync<ChecklistPage>(parentPath, ct))!;
        Assert.Equal(4, current.CardVersion); Assert.Equal(3, Assert.Single(current.Items).Checklist.Version);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var lostReplay = await Mutate(member, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, lostReplay.StatusCode);
    }

    [Fact]
    public async Task PRD_13_Checklist_creation_is_authorized_CAS_retry_safe_and_empty_progress_is_zero()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Checklist Card", "Original description", null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/checklists";
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path, ct)).StatusCode);
        using var forbidden = await Mutate(outsider, HttpMethod.Post, path, new CreateChecklistInput("Protected title", 1));
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        foreach (var title in new string?[] { null, "", "\t", "bad\0text", new('x', 161) })
        {
            using var invalid = await Mutate(owner, HttpMethod.Post, path, new CreateChecklistInput(title, 1));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var invalidVersion = await Mutate(owner, HttpMethod.Post, path, new CreateChecklistInput("Title", 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode); Assert.Equal(card, await store.FindCardAsync(card.Id, ct));
        Assert.Empty((await owner.GetFromJsonAsync<ChecklistPage>(path, ct))!.Items);
        var key = Guid.NewGuid().ToString(); var input = new CreateChecklistInput(" Preparations ", 1);
        using var created = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var change = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!;
        Assert.Equal(2, change.CardVersion); Assert.Equal(1, change.Checklist.Version); Assert.Equal("Preparations", change.Checklist.Title);
        Assert.Equal(card.Id, change.Checklist.CardId); Assert.Equal(f.Organization, change.Checklist.OrganizationId);
        var page = (await member.GetFromJsonAsync<ChecklistPage>(path, ct))!;
        Assert.Single(page.Items); Assert.True(page.CanEdit); Assert.Equal(0, page.Items[0].Percent);
        using var second = await Mutate(member, HttpMethod.Post, path, new CreateChecklistInput("Second", 2));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(await created.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var reused = await Mutate(owner, HttpMethod.Post, path, input with { Title = "Different" }, key);
        using var stale = await Mutate(owner, HttpMethod.Post, path, new CreateChecklistInput("Stale", 1));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = (await store.FindCardAsync(card.Id, ct))!;
        Assert.Equal(3, current.Version); Assert.Equal(card.Title, current.Title); Assert.Equal(card.Description, current.Description);
        Assert.Equal(card.DueAt, current.DueAt); Assert.Equal(card.DueComplete, current.DueComplete);
        var two = (await owner.GetFromJsonAsync<ChecklistPage>(path, ct))!; Assert.Equal(2, two.Items.Count);
        Assert.True(string.CompareOrdinal(two.Items[0].Checklist.Rank, two.Items[1].Checklist.Rank) < 0);
        var board = (await store.FindBoardAsync(f.Board, ct))!;
        using var archived = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = board.Version });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.False((await owner.GetFromJsonAsync<ChecklistPage>(path, ct))!.CanEdit);
        using var retiredReplay = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, retiredReplay.StatusCode);
    }
    [Fact]
    public async Task PRD_13_Checklist_pages_are_ordered_bounded_Card_scoped_and_revalidate_revocation()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Paged checklists", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/checklists";
        for (var index = 0; index < 63; index++)
        {
            using var result = await Mutate(owner, HttpMethod.Post, path, new CreateChecklistInput($"Checklist {index}", index + 1));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }
        var first = (await member.GetFromJsonAsync<ChecklistPage>(path, ct))!;
        Assert.Equal(50, first.Items.Count); Assert.NotNull(first.NextCursor); Assert.Equal(64, first.CardVersion);
        var second = (await member.GetFromJsonAsync<ChecklistPage>($"{path}?after={Uri.EscapeDataString(first.NextCursor)}", ct))!;
        Assert.Equal(13, second.Items.Count); Assert.Null(second.NextCursor);
        Assert.Equal(63, first.Items.Concat(second.Items).Select(value => value.Checklist.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 63).Select(index => $"Checklist {index}"), first.Items.Concat(second.Items).Select(value => value.Checklist.Title));
        var wrongParent = $"{Guid.NewGuid():D}/{first.Items[^1].Checklist.Rank}/{first.Items[^1].Checklist.Id:D}";
        Assert.Equal(HttpStatusCode.BadRequest, (await member.GetAsync($"{path}?after={Uri.EscapeDataString(wrongParent)}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{path}?after=malformed", ct)).StatusCode);
        using var remove = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{path}?after={Uri.EscapeDataString(first.NextCursor)}", ct)).StatusCode);
    }
}
