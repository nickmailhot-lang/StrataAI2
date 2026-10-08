using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("board")]
    [InlineData("list")]
    [InlineData("card")]
    public async Task Http_restoration_retains_archive_history_and_returns_work_to_active_reads(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Archive history Card" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var route = kind switch { "board" => $"/boards/{f.Board}", "list" => $"/lists/{f.List}", _ => $"/cards/{card}" };
        using var archive = await Mutate(owner, HttpMethod.Post, route + "/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var archivedAt = (await archive.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("archivedAt").GetDateTimeOffset();
        using var restore = await Mutate(owner, HttpMethod.Post, route + "/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var restored = await restore.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(archivedAt, restored.GetProperty("archivedAt").GetDateTimeOffset());
        Assert.Equal(3, restored.GetProperty("version").GetInt64());
        Assert.False(restored.TryGetProperty("deletedAt", out _)); Assert.False(restored.TryGetProperty("deletedBy", out _));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var snapshot = await owner.GetFromJsonAsync<BoardSnapshot>($"/boards/{f.Board}", json, ct);
        Assert.NotNull(snapshot); Assert.Equal(BoardLifecycleState.Active, snapshot.Board.LifecycleState);
        var activeList = Assert.Single(snapshot.Lists, row => row.List.Id == f.List);
        Assert.Equal(WorkItemLifecycleState.Active, activeList.List.LifecycleState);
        var activeCard = Assert.Single(activeList.Cards, row => row.Id == card);
        Assert.Equal(WorkItemLifecycleState.Active, activeCard.LifecycleState);
        var retainedAt = kind switch { "board" => snapshot.Board.ArchivedAt, "list" => activeList.List.ArchivedAt, _ => activeCard.ArchivedAt };
        Assert.Equal(archivedAt, retainedAt);
    }

    [Fact]
    public async Task Authorized_card_deletion_persists_actor_and_rejected_deletion_does_not_attribute()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var service = app.Services.GetRequiredService<IWorkManagementService>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Deletion attribution", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await service.SetCardLifecycleAsync(card.Id, f.Owner, WorkItemLifecycleState.Archived, 1, "actor-test", ct)).Succeeded);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Archived,
            WorkItemLifecycleState.Deleted, 2, DateTimeOffset.UtcNow, ct));
        Assert.Null((await store.FindCardAsync(card.Id, ct))!.DeletedBy);
        Assert.False((await service.SetCardLifecycleAsync(card.Id, f.Owner, WorkItemLifecycleState.Deleted, 2, "actor-test", ct)).Succeeded);
        Assert.Null((await store.FindCardAsync(card.Id, ct))!.DeletedBy);
        var deletion = await service.SetCardLifecycleAsync(card.Id, f.Owner, WorkItemLifecycleState.Deleted, 2, "actor-test", ct, deletionConfirmed: true);
        Assert.True(deletion.Succeeded);
        Assert.Equal(f.Owner, (await store.FindCardAsync(card.Id, ct, includeDeleted: true))!.DeletedBy);
    }

    [Fact]
    public async Task Demo_lifecycle_records_retain_latest_archive_clock_through_restoration_and_deletion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var at = DateTimeOffset.UtcNow;
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Lifecycle times", null, null, at, ct);
        var archived = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, at.AddSeconds(1), ct);
        Assert.Equal(at.AddSeconds(1), archived!.ArchivedAt); Assert.Null(archived.DeletedAt);
        var restored = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Active, 2, at.AddSeconds(2), ct);
        Assert.Equal(archived.ArchivedAt, restored!.ArchivedAt); Assert.Equal(WorkItemLifecycleState.Active, restored.LifecycleState);
        Assert.Null(restored.DeletedAt); Assert.Null(restored.DeletedBy); Assert.Equal(3, restored.Version);
        var rearchived = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 3, at.AddSeconds(3), ct);
        var deleted = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted, 4, at.AddSeconds(4), ct, f.Owner);
        Assert.Equal(rearchived!.ArchivedAt, deleted!.ArchivedAt); Assert.Equal(at.AddSeconds(4), deleted.DeletedAt); Assert.Equal(f.Owner, deleted.DeletedBy);
        var list = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, at.AddSeconds(5), ct);
        var restoredList = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Active, 2, at.AddSeconds(6), ct);
        Assert.Equal(list!.ArchivedAt, restoredList!.ArchivedAt); Assert.Equal(WorkItemLifecycleState.Active, restoredList.LifecycleState);
        Assert.Null(restoredList.DeletedAt); Assert.Null(restoredList.DeletedBy); Assert.Equal(3, restoredList.Version);
        var rearchivedList = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 3, at.AddSeconds(7), ct);
        var deletedList = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted, 4, at.AddSeconds(8), ct, f.Owner);
        Assert.Equal(at.AddSeconds(7), rearchivedList!.ArchivedAt);
        Assert.Equal(rearchivedList.ArchivedAt, deletedList!.ArchivedAt); Assert.Equal(at.AddSeconds(8), deletedList.DeletedAt); Assert.Equal(f.Owner, deletedList.DeletedBy);
        var board = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, at.AddSeconds(9), ct);
        var restoredBoard = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Archived, BoardLifecycleState.Active, 2, at.AddSeconds(10), ct);
        Assert.Equal(board!.ArchivedAt, restoredBoard!.ArchivedAt); Assert.Equal(BoardLifecycleState.Active, restoredBoard.LifecycleState);
        Assert.Null(restoredBoard.DeletedAt); Assert.Null(restoredBoard.DeletedBy); Assert.Equal(3, restoredBoard.Version);
        var rearchivedBoard = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Active, BoardLifecycleState.Archived, 3, at.AddSeconds(11), ct);
        var deletedBoard = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Archived, BoardLifecycleState.Deleted, 4, at.AddSeconds(12), ct, f.Owner);
        Assert.Equal(at.AddSeconds(11), rearchivedBoard!.ArchivedAt);
        Assert.Equal(rearchivedBoard.ArchivedAt, deletedBoard!.ArchivedAt); Assert.Equal(at.AddSeconds(12), deletedBoard.DeletedAt); Assert.Equal(f.Owner, deletedBoard.DeletedBy);
    }
}
