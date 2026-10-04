using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
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
        Assert.Null(restored!.ArchivedAt); Assert.Null(restored.DeletedAt);
        var rearchived = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 3, at.AddSeconds(3), ct);
        var deleted = await store.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted, 4, at.AddSeconds(4), ct);
        Assert.Equal(rearchived!.ArchivedAt, deleted!.ArchivedAt); Assert.Equal(at.AddSeconds(4), deleted.DeletedAt);
        var list = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, at.AddSeconds(5), ct);
        var deletedList = await store.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted, 2, at.AddSeconds(6), ct);
        Assert.Equal(list!.ArchivedAt, deletedList!.ArchivedAt); Assert.Equal(at.AddSeconds(6), deletedList.DeletedAt);
        var board = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, at.AddSeconds(7), ct);
        var deletedBoard = await store.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Archived, BoardLifecycleState.Deleted, 2, at.AddSeconds(8), ct);
        Assert.Equal(board!.ArchivedAt, deletedBoard!.ArchivedAt); Assert.Equal(at.AddSeconds(8), deletedBoard.DeletedAt);
    }
}
