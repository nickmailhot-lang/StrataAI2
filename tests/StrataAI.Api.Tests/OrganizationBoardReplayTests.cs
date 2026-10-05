using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
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
        var seeded = await unit.ExecuteReadAsync(f.Organization, null, "fixture_denied", () => Task.FromResult(true), async () =>
        {
            for (var index = 0; index < 64; index++)
                await source.AppendAsync(new(Guid.NewGuid(), f.Organization, hidden, f.Owner, "BOARD_UPDATED", "Board", hidden,
                    index + 2, "organization-replay-fixture", at), ct);
            await source.AppendAsync(new(visibleId, f.Organization, f.Board, f.Owner, "BOARD_ARCHIVED", "Board", f.Board, 2,
                "organization-replay-fixture", at), ct);
            return WorkOperation<bool>.Success(true);
        }, ct);
        Assert.True(seeded.Succeeded);
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
            await source.AppendAsync(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "BOARD_RESTORED", "Board", f.Board, 3,
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
