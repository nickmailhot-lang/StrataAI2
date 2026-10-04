using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Completion_filter_uses_canonical_due_state_composes_ANY_ALL_and_preserves_admission_and_parent_lifecycle()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var dates = app.Services.GetRequiredService<ICardDateStore>(); var now = DateTimeOffset.UtcNow;
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Completion filters", null, now, ct);
        var complete = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Completed deadline", null, null, now, ct);
        Assert.NotNull(await dates.SetDatesAsync(fixture.Board.OrganizationId, fixture.Board.Id, complete.Id,
            new(null, now.AddDays(1), "UTC", true, true), 1, now, ct));
        var incomplete = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "No deadline", null, null, now, ct);
        var selected = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, completion: "complete");
        Assert.True(selected.Succeeded); Assert.Equal(complete.Id, Assert.Single(selected.Value!.Items).Id);
        var unfinished = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, completion: "incomplete");
        Assert.True(unfinished.Succeeded); Assert.Equal(incomplete.Id, Assert.Single(unfinished.Value!.Items).Id);
        var all = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, "No deadline", [], "all", cancellationToken: ct, completion: "complete");
        Assert.True(all.Succeeded); Assert.Empty(all.Value!.Items);
        var any = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, "No deadline", [], "any", cancellationToken: ct, completion: "complete");
        Assert.True(any.Succeeded); Assert.Equal(new[] { complete.Id, incomplete.Id }.Order(), any.Value!.Items.Select(c => c.Id));
        Assert.Equal("invalid_board_filter", (await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, completion: "unknown")).ErrorCode);
        Assert.Equal("board_not_found", (await work.FilterBoardCardsAsync(fixture.Board.Id, Guid.NewGuid(), null, [], "all", cancellationToken: ct, completion: "unknown")).ErrorCode);
        Assert.True((await work.SetListLifecycleAsync(list.Id, fixture.Owner.Id, WorkItemLifecycleState.Archived, 1, "completion-fixture", ct)).Succeeded);
        var archived = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, completion: "complete");
        Assert.True(archived.Succeeded); Assert.Empty(archived.Value!.Items);
    }
}
