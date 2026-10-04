using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Deadline_filters_exclude_completed_dates_and_compose_with_keyword_and_completion()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct); var now = DateTimeOffset.UtcNow;
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var dates = app.Services.GetRequiredService<ICardDateStore>();
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Deadline filters", null, now, ct);
        var expected = new Dictionary<string, Guid>();
        foreach (var state in new[] { "none", "overdue", "upcoming", "completed" })
        {
            var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), state, null, null, now, ct); expected[state] = card.Id;
            if (state != "none") Assert.NotNull(await dates.SetDatesAsync(fixture.Board.OrganizationId, fixture.Board.Id, card.Id,
                new(null, state == "overdue" ? now.AddDays(-1) : now.AddDays(1), "UTC", true, state == "completed"), 1, now, ct));
        }
        foreach (var state in new[] { "none", "overdue", "upcoming" })
        {
            var result = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, due: state);
            Assert.True(result.Succeeded); Assert.Equal(expected[state], Assert.Single(result.Value!.Items).Id);
        }
        var incompatible = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, completion: "complete", due: "overdue");
        Assert.True(incompatible.Succeeded); Assert.Empty(incompatible.Value!.Items);
        var any = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, "none", [], "any", cancellationToken: ct, due: "overdue");
        Assert.True(any.Succeeded); Assert.Equal(new[] { expected["none"], expected["overdue"] }.Order(), any.Value!.Items.Select(c => c.Id));
        Assert.Equal("invalid_board_filter", (await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, due: "unknown")).ErrorCode);
        Assert.Equal("board_not_found", (await work.FilterBoardCardsAsync(fixture.Board.Id, Guid.NewGuid(), null, [], "all", cancellationToken: ct, due: "unknown")).ErrorCode);
    }

    [Fact]
    public async Task Completion_predicate_precedes_page_limit_and_seek_returns_each_matching_Card_once()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var dates = app.Services.GetRequiredService<ICardDateStore>(); var now = DateTimeOffset.UtcNow;
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Completion paging", null, now, ct);
        for (var n = 1; n <= 5; n++)
            await store.CreateCardAsync(list.Id, Guid.Parse($"00000000-0000-4000-8000-{n:000000000000}"), "Earlier incomplete", null, null, now, ct);
        var ids = new List<Guid>();
        for (var n = 0; n < 52; n++)
        {
            var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Completed page member", null, null, now, ct); ids.Add(card.Id);
            Assert.NotNull(await dates.SetDatesAsync(fixture.Board.OrganizationId, fixture.Board.Id, card.Id,
                new(null, now.AddDays(1), "UTC", true, true), 1, now, ct));
        }
        var first = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, completion: "complete");
        Assert.True(first.Succeeded); Assert.Equal(50, first.Value!.Items.Count); Assert.NotNull(first.Value.NextCursor);
        var tail = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", first.Value.NextCursor, ct, completion: "complete");
        Assert.True(tail.Succeeded); Assert.Equal(2, tail.Value!.Items.Count); Assert.Null(tail.Value.NextCursor);
        var returned = first.Value.Items.Concat(tail.Value.Items).ToArray();
        Assert.All(returned, c => Assert.True(c.DueComplete));
        Assert.Equal(ids.Order(), returned.Select(c => c.Id));
        Assert.Equal(52, returned.Select(c => c.Id).Distinct().Count());
    }

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
