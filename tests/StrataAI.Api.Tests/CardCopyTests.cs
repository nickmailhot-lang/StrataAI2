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
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_08_Copy_creates_independent_content_without_inherited_history_and_recovers_original_receipt(bool crossBoard)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var children = app.Services.GetRequiredService<IChecklistStore>();
        var now = DateTimeOffset.UtcNow;
        var source = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Copy source", "Retained description", null, now, ct);
        var checklist = await children.CreateAsync(f.Organization, source.Id, "Fresh work", RankToken.After(null), now, ct);
        var originals = new List<ChecklistItemRecord>(); string? rank = null;
        for (var i = 0; i < 63; i++)
        {
            rank = RankToken.After(rank); originals.Add(await children.CreateItemAsync(f.Organization, checklist.Id, $"Work {i}", rank, now, ct));
        }
        originals[0] = (await children.UpdateItemAsync(f.Organization, checklist.Id, originals[0].Id, originals[0].Text, true, now, f.Owner, 1, now, ct))!;
        await children.DeleteItemAsync(f.Organization, checklist.Id, originals[^1].Id, 1, now, ct);
        var removed = await children.CreateAsync(f.Organization, source.Id, "Deleted checklist", RankToken.After(checklist.Rank), now, ct);
        await children.CreateItemAsync(f.Organization, removed.Id, "Hidden work", RankToken.After(null), now, ct);
        await children.DeleteAsync(f.Organization, source.Id, removed.Id, 1, f.Owner, "copy-fixture", now, ct);
        using var labelResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/labels", new { name = "Independent label", color = "blue" });
        var label = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var assigned = await Mutate(owner, HttpMethod.Put, $"/cards/{source.Id}/labels/{label}?version=1", new { });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{source.Id}/dates",
            new CardDatesInput(null, now.AddDays(2).ToString("O"), "UTC", true, true, 2));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        using var comment = await Mutate(member, HttpMethod.Post, $"/cards/{source.Id}/comments", new CreateCardCommentInput("Source history only", 3));
        Assert.Equal(HttpStatusCode.OK, comment.StatusCode);
        using var watch = await Mutate(member, HttpMethod.Put, $"/watch/CARD/{source.Id}?version=0", new { }); Assert.Equal(HttpStatusCode.OK, watch.StatusCode);
        using var membership = await Mutate(owner, HttpMethod.Put, $"/cards/{source.Id}/members/{f.Recipient}?version=4", new { });
        Assert.Equal(HttpStatusCode.OK, membership.StatusCode);
        var before = (await work.FindCardAsync(source.Id, ct))!;
        var destinationBoard = f.Board; var destinationList = f.List;
        if (crossBoard)
        {
            using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Copy destination", visibility = "PRIVATE" });
            destinationBoard = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{destinationBoard}/members/{f.Recipient}", new { role = "MEMBER" });
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
            using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{destinationBoard}/lists", new { name = "Copy destination" });
            destinationList = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        }
        using var boardWatch = await Mutate(owner, HttpMethod.Put, $"/watch/BOARD/{destinationBoard}?version=0", new { }); Assert.Equal(HttpStatusCode.OK, boardWatch.StatusCode);
        var input = new { sourceBoardId = f.Board, destinationListId = destinationList, title = "  Independent copy  ", expectedVersion = before.Version };
        var key = Guid.NewGuid().ToString(); var path = $"/cards/{source.Id}/copy";
        using var response = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var acknowledgment = await response.Content.ReadAsStringAsync(ct); var copy = (await response.Content.ReadFromJsonAsync<CardRecord>(json, ct))!;
        Assert.NotEqual(source.Id, copy.Id); Assert.Equal(destinationBoard, copy.BoardId); Assert.Equal(destinationList, copy.ListId);
        Assert.Equal("Independent copy", copy.Title); Assert.Equal(before.Description, copy.Description); Assert.Equal(1, copy.Version);
        Assert.Equal(before.DueAt, copy.DueAt); Assert.Equal(before.DueTimezone, copy.DueTimezone); Assert.Equal(before.DueHasTime, copy.DueHasTime);
        Assert.False(copy.DueComplete); Assert.Equal(before, await work.FindCardAsync(source.Id, ct));
        var labels = await member.GetFromJsonAsync<JsonElement>($"/cards/{copy.Id}/labels", ct);
        var copiedLabel = Assert.Single(labels.GetProperty("items").EnumerateArray());
        Assert.Equal(crossBoard, copiedLabel.GetProperty("id").GetGuid() != label); Assert.Equal(destinationBoard, copiedLabel.GetProperty("boardId").GetGuid());
        var page = (await member.GetFromJsonAsync<ChecklistPage>($"/cards/{copy.Id}/checklists", ct))!;
        var parent = Assert.Single(page.Items).Checklist; Assert.NotEqual(checklist.Id, parent.Id); Assert.Equal(1, parent.Version);
        var itemPath = $"/cards/{copy.Id}/checklists/{parent.Id}/items";
        var first = (await member.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!;
        var second = (await member.GetFromJsonAsync<ChecklistItemPage>(itemPath + "?after=" + first.NextCursor, ct))!;
        Assert.Equal(62, first.Summary.Total); Assert.Equal(0, first.Summary.Completed); Assert.Equal(50, first.Items.Count); Assert.Equal(12, second.Items.Count);
        foreach (var (item, index) in first.Items.Concat(second.Items).Select((item, index) => (item, index)))
        {
            Assert.NotEqual(originals[index].Id, item.Id); Assert.Equal(originals[index].Text, item.Text); Assert.Equal(originals[index].Rank, item.Rank);
            Assert.False(item.Completed); Assert.Null(item.CompletedAt); Assert.Null(item.CompletedBy); Assert.Equal(1, item.Version);
        }
        Assert.Empty((await member.GetFromJsonAsync<CardCommentPage>($"/cards/{copy.Id}/comments", ct))!.Items);
        Assert.False((await member.GetFromJsonAsync<WatchState>($"/watch/CARD/{copy.Id}", ct))!.Watching);
        Assert.Empty((await member.GetFromJsonAsync<JsonElement>($"/cards/{copy.Id}/members", ct)).GetProperty("items").EnumerateArray());
        var history = await member.GetFromJsonAsync<JsonElement>($"/cards/{copy.Id}/activity", ct);
        Assert.Equal("CARD_COPIED", Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("eventType").GetString());
        var notices = await app.Services.GetRequiredService<IWorkNotificationStore>().ListCardNotificationsAsync(f.Organization, f.Owner, cancellationToken: ct);
        Assert.Equal("CARD_COPIED", Assert.Single(notices, n => n.CardId == copy.Id).NotificationType);
        using var renameSource = await Mutate(owner, HttpMethod.Patch, $"/cards/{source.Id}", new { title = "Later source", version = before.Version });
        Assert.Equal(HttpStatusCode.OK, renameSource.StatusCode);
        using var renameCopy = await Mutate(member, HttpMethod.Patch, $"/cards/{copy.Id}", new { title = "Later copied title", description = copy.Description, version = 1 });
        Assert.Equal(HttpStatusCode.OK, renameCopy.StatusCode);
        if (crossBoard)
        {
            using var thirdBoard = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Current copied context" });
            var third = (await thirdBoard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{third}/members/{f.Recipient}", new { role = "MEMBER" });
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
            using var thirdList = await Mutate(owner, HttpMethod.Post, $"/boards/{third}/lists", new { name = "Current copied parent" });
            var list = (await thirdList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            using var movedCopy = await Mutate(member, HttpMethod.Post, $"/cards/{copy.Id}/move",
                new { sourceBoardId = destinationBoard, destinationListId = list, expectedVersion = 2 });
            Assert.Equal(HttpStatusCode.OK, movedCopy.StatusCode);
            using var movedSource = await Mutate(member, HttpMethod.Post, $"/cards/{source.Id}/move",
                new { sourceBoardId = f.Board, destinationListId = destinationList, expectedVersion = before.Version + 1 });
            Assert.Equal(HttpStatusCode.OK, movedSource.StatusCode);
            using var revokeCurrentCopy = await Mutate(owner, HttpMethod.Delete, $"/boards/{third}/members/{f.Recipient}", new { });
            Assert.Equal(HttpStatusCode.NoContent, revokeCurrentCopy.StatusCode);
            using var hiddenCurrentCopy = await Mutate(member, HttpMethod.Post, path, input, key);
            Assert.Equal(HttpStatusCode.NotFound, hiddenCurrentCopy.StatusCode);
            using var restoreCurrentCopy = await Mutate(owner, HttpMethod.Patch, $"/boards/{third}/members/{f.Recipient}", new { role = "MEMBER" });
            Assert.Equal(HttpStatusCode.OK, restoreCurrentCopy.StatusCode);
        }
        using var replay = await Mutate(member, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(acknowledgment, await replay.Content.ReadAsStringAsync(ct));
        using var changed = await Mutate(member, HttpMethod.Post, path,
            new { sourceBoardId = f.Board, destinationListId = destinationList, title = "Different copy", expectedVersion = before.Version }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var remove = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }); Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var revoked = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.DoesNotContain("Independent copy", await revoked.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task PRD_08_Copy_refuses_hidden_foreign_stale_invalid_and_archived_sources_without_effects()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var source = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Private source", null, null, DateTimeOffset.UtcNow, ct);
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Hidden target" });
        var hidden = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{hidden}/lists", new { name = "Hidden target" });
        var target = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var foreignBoard = await TelemetryBoard(owner, ct);
        using var foreignListResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{foreignBoard}/lists", new { name = "Foreign target" });
        var foreignList = (await foreignListResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        foreach (var (actor, board, list, title, version, status) in new[] {
            (member,f.Board,target,"Copy",1L,HttpStatusCode.NotFound),
            (owner,f.Board,foreignList,"Copy",1L,HttpStatusCode.NotFound),
            (owner,Guid.NewGuid(),f.List,"Copy",1L,HttpStatusCode.NotFound),
            (owner,f.Board,f.List,"Copy",2L,HttpStatusCode.Conflict),
            (owner,f.Board,f.List," ",1L,HttpStatusCode.BadRequest) })
        {
            using var denied = await Mutate(actor, HttpMethod.Post, $"/cards/{source.Id}/copy",
                new { sourceBoardId = board, destinationListId = list, title, expectedVersion = version });
            Assert.Equal(status, denied.StatusCode);
        }
        Assert.Equal(source, await work.FindCardAsync(source.Id, ct));
        var snapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{f.Board}", ct);
        Assert.Single(snapshot.GetProperty("lists").EnumerateArray().SelectMany(l => l.GetProperty("cards").EnumerateArray()));
        using var archive = await Mutate(owner, HttpMethod.Post, $"/cards/{source.Id}/archive", new { version = 1 }); Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var refused = await Mutate(owner, HttpMethod.Post, $"/cards/{source.Id}/copy",
            new { sourceBoardId = f.Board, destinationListId = f.List, title = "Copy", expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }
}
