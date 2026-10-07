using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Ordinary_demo_reader_preserves_sources_and_resets_on_actual_access_changes_with_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var replay = app.Services.GetRequiredKeyedService<TransactionalOrganizationBoardSynchronization>(OrganizationBoardAudience.BoardDiscovery);
        var archive = app.Services.GetRequiredService<TransactionalOrganizationBoardSynchronization>();
        var source = app.Services.GetRequiredService<IWorkEventStore>();
        var unit = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var at = DateTimeOffset.UtcNow; var hidden = Guid.NewGuid(); var visibleId = Guid.NewGuid();
        await store.CreateBoardAsync(f.Organization, f.Owner, hidden, "Private discovery fixture", null,
            BoardVisibility.Private, "COLOR", "blue", at, ct);
        await store.UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Member, at, ct);
        var initial = await replay.ReadAsync(f.Organization, f.Recipient, null, cancellationToken: ct);
        Assert.True(initial.Succeeded);
        // Legacy source fixtures exercise replay filtering only. They run outside
        // an owning command and do not fabricate recipient authority history.
        {
            for (var index = 0; index < 64; index++)
                await source.AppendAsync(new(Guid.NewGuid(), f.Organization, hidden, f.Owner, "BOARD_UPDATED", "Board", hidden,
                    index + 2, "discovery-fixture", at), ct);
            await source.AppendAsync(new(visibleId, f.Organization, f.Board, f.Owner, "BOARD_UPDATED", "Board", f.Board, 2,
                "discovery-fixture", at), ct);
        }
        var page = await replay.ReadAsync(f.Organization, f.Recipient, initial.Value!.Cursor, cancellationToken: ct);
        Assert.True(page.Succeeded); Assert.False(page.Value!.ResetRequired); Assert.False(page.Value.HasMore);
        Assert.Equal(visibleId, Assert.Single(page.Value.Events).EventId);
        var wrongAudience = await archive.ReadAsync(f.Organization, f.Recipient, page.Value.Cursor, cancellationToken: ct);
        Assert.True(wrongAudience.Succeeded); Assert.True(wrongAudience.Value!.ResetRequired); Assert.Empty(wrongAudience.Value.Events);
        // The shared permission binding conservatively resets on promotion;
        // the ordinary reader remains admitted after accepting the new binding.
        await store.UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Admin, at, ct);
        var promoted = await replay.ReadAsync(f.Organization, f.Recipient, page.Value.Cursor, cancellationToken: ct);
        Assert.True(promoted.Succeeded); Assert.True(promoted.Value!.ResetRequired);
        var stable = await replay.ReadAsync(f.Organization, f.Recipient, promoted.Value.Cursor, cancellationToken: ct);
        Assert.True(stable.Succeeded); Assert.False(stable.Value!.ResetRequired);
        var board = await store.FindBoardAsync(f.Board, ct); Assert.NotNull(board);
        var refused = await unit.ExecuteReadAsync(f.Organization, null, "fixture_denied", () => Task.FromResult(true), async () =>
        {
            await store.SetBoardVisibilityAsync(f.Board, BoardVisibility.Private, board.Version, at, ct);
            await store.RemoveBoardMemberAsync(f.Board, f.Recipient, at, ct);
            return WorkOperation<bool>.Failure("declared_late_refusal");
        }, ct);
        Assert.Equal("declared_late_refusal", refused.ErrorCode);
        var unchanged = await replay.ReadAsync(f.Organization, f.Recipient, stable.Value.Cursor, cancellationToken: ct);
        Assert.True(unchanged.Succeeded); Assert.False(unchanged.Value!.ResetRequired); Assert.Empty(unchanged.Value.Events);
        await store.SetBoardVisibilityAsync(f.Board, BoardVisibility.Private, board.Version, at, ct);
        await store.RemoveBoardMemberAsync(f.Board, f.Recipient, at, ct);
        var withdrawn = await replay.ReadAsync(f.Organization, f.Recipient, unchanged.Value.Cursor, cancellationToken: ct);
        Assert.True(withdrawn.Succeeded); Assert.True(withdrawn.Value!.ResetRequired); Assert.Empty(withdrawn.Value.Events);
    }

    [Fact]
    public async Task Organization_Board_demo_replay_filters_before_bound_preserves_source_and_rolls_back_projection_and_grant_revision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var hidden = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
        await store.CreateBoardAsync(f.Organization, f.Owner, hidden, "Hidden replay Board", "Protected replay body",
            BoardVisibility.Private, "COLOR", "blue", at, ct);
        await store.UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Admin, at, ct);
        var replay = app.Services.GetRequiredService<TransactionalOrganizationBoardSynchronization>();
        var initial = await replay.ReadAsync(f.Organization, f.Recipient, null, cancellationToken: ct);
        Assert.True(initial.Succeeded); Assert.True(initial.Value!.ResetRequired);
        var ownerInitial = await replay.ReadAsync(f.Organization, f.Owner, null, cancellationToken: ct);
        Assert.True(ownerInitial.Succeeded);
        var source = app.Services.GetRequiredService<IWorkEventStore>();
        var unit = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var visibleId = Guid.NewGuid();
        // Declared synthetic canonical sources prove adapter/query behavior;
        // this does not claim actual Board lifecycle or Worker delivery.
        // Legacy source fixtures exercise replay filtering only. They run outside
        // an owning command and do not fabricate recipient authority history.
        {
            for (var index = 0; index < 64; index++)
                await source.AppendAsync(new(Guid.NewGuid(), f.Organization, hidden, f.Owner, "BOARD_UPDATED", "Board", hidden,
                    index + 2, "organization-replay-fixture", at), ct);
            await source.AppendAsync(new(visibleId, f.Organization, f.Board, f.Owner, "BOARD_ARCHIVED", "Board", f.Board, 2,
                "organization-replay-fixture", at), ct);
        }
        var page = await replay.ReadAsync(f.Organization, f.Recipient, initial.Value.Cursor, cancellationToken: ct);
        Assert.True(page.Succeeded); Assert.False(page.Value!.ResetRequired); Assert.False(page.Value.HasMore);
        var actual = Assert.Single(page.Value.Events); Assert.Equal(visibleId, actual.EventId); Assert.Equal(f.Board, actual.BoardId);
        Assert.Equal("BOARD_ARCHIVED", actual.EventType);
        var boundedOwner = await replay.ReadAsync(f.Organization, f.Owner, ownerInitial.Value!.Cursor, cancellationToken: ct);
        Assert.True(boundedOwner.Succeeded); Assert.Equal(50, boundedOwner.Value!.Events.Count); Assert.True(boundedOwner.Value.HasMore);
        var tail = await replay.ReadAsync(f.Organization, f.Owner, boundedOwner.Value.Cursor, cancellationToken: ct);
        Assert.True(tail.Succeeded); Assert.Equal(15, tail.Value!.Events.Count); Assert.False(tail.Value.HasMore);
        Assert.Equal(65, boundedOwner.Value.Events.Concat(tail.Value.Events).Select(e => e.EventId).Distinct().Count());

        var refused = await unit.ExecuteReadAsync(f.Organization, null, "fixture_denied", () => Task.FromResult(true), async () =>
        {
            await store.UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Member, at, ct);
            var changed = await store.UpdateBoardAsync(f.Board, "Tentative replay Board", null, "COLOR", null, 1, at, ct);
            Assert.NotNull(changed);
            await source.AppendAsync(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "BOARD_UPDATED", "Board", f.Board, changed.Version,
                "organization-replay-rollback", at), ct);
            return WorkOperation<bool>.Failure("declared_late_refusal");
        }, ct);
        Assert.Equal("declared_late_refusal", refused.ErrorCode);
        var unchanged = await replay.ReadAsync(f.Organization, f.Recipient, page.Value.Cursor, cancellationToken: ct);
        Assert.True(unchanged.Succeeded); Assert.False(unchanged.Value!.ResetRequired); Assert.Empty(unchanged.Value.Events);
        Assert.Equal(BoardRole.Admin, (await store.FindBoardMemberAsync(f.Board, f.Recipient, ct))!.Role);
        await store.UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Member, at, ct);
        var withdrawn = await replay.ReadAsync(f.Organization, f.Recipient, page.Value.Cursor, cancellationToken: ct);
        Assert.True(withdrawn.Succeeded); Assert.True(withdrawn.Value!.ResetRequired); Assert.Empty(withdrawn.Value.Events);
    }
}
