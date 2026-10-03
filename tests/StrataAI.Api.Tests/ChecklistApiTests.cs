using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
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
