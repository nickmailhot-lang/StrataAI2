using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
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
    public async Task Demo_lifecycle_records_retain_archive_and_deletion_times_and_reset_restoration()
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
        Assert.Null(restored!.ArchivedAt); Assert.Null(restored.DeletedAt); Assert.Null(restored.DeletedBy);
        var rearchived = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 3, at.AddSeconds(3), ct);
        var deleted = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted, 4, at.AddSeconds(4), ct, f.Owner);
        Assert.Equal(rearchived!.ArchivedAt, deleted!.ArchivedAt); Assert.Equal(at.AddSeconds(4), deleted.DeletedAt); Assert.Equal(f.Owner, deleted.DeletedBy);
        var list = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, at.AddSeconds(5), ct);
        var deletedList = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted, 2, at.AddSeconds(6), ct, f.Owner);
        Assert.Equal(list!.ArchivedAt, deletedList!.ArchivedAt); Assert.Equal(at.AddSeconds(6), deletedList.DeletedAt); Assert.Equal(f.Owner, deletedList.DeletedBy);
        var board = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, at.AddSeconds(7), ct);
        var deletedBoard = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Archived, BoardLifecycleState.Deleted, 2, at.AddSeconds(8), ct, f.Owner);
        Assert.Equal(board!.ArchivedAt, deletedBoard!.ArchivedAt); Assert.Equal(at.AddSeconds(8), deletedBoard.DeletedAt); Assert.Equal(f.Owner, deletedBoard.DeletedBy);
    }
}
