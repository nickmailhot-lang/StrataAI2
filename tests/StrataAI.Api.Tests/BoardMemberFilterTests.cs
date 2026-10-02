using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Member_filters_page_persisted_assignments_combine_ANY_ALL_and_exclude_departed_members_and_archived_parents()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct); var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var now = DateTimeOffset.UtcNow;
        await store.UpsertBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, BoardRole.Member, now, ct);
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Member filters", null, now, ct);
        var ids = new List<Guid>();
        for (var i = 0; i < 52; i++)
        {
            var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), i == 0 ? "Both members" : "Assigned Card", null, null, now, ct); ids.Add(card.Id);
            Assert.True((await work.SetCardMemberAsync(card.Id, fixture.Recipient.Id, fixture.Owner.Id, true, 1, "fixture", ct)).Succeeded);
            if (i == 0) Assert.True((await work.SetCardMemberAsync(card.Id, fixture.Owner.Id, fixture.Owner.Id, true, 2, "fixture", ct)).Succeeded);
        }
        await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Unassigned Card", null, null, now, ct);
        var first = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, memberIds: [fixture.Recipient.Id]);
        Assert.True(first.Succeeded); Assert.Equal(50, first.Value!.Items.Count);
        var second = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", first.Value.NextCursor, ct, [fixture.Recipient.Id]);
        Assert.True(second.Succeeded); Assert.Equal(2, second.Value!.Items.Count); Assert.Null(second.Value.NextCursor);
        Assert.Equal(ids.Order(), first.Value.Items.Concat(second.Value.Items).Select(c => c.Id));
        var both = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, memberIds: [fixture.Owner.Id, fixture.Recipient.Id]);
        Assert.True(both.Succeeded); Assert.Equal(ids[0], Assert.Single(both.Value!.Items).Id);
        var noKeyword = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, "absent", [], "all", cancellationToken: ct, memberIds: [fixture.Recipient.Id]);
        Assert.True(noKeyword.Succeeded); Assert.Empty(noKeyword.Value!.Items);
        var anyKeyword = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, "absent", [], "any", cancellationToken: ct, memberIds: [fixture.Recipient.Id]);
        Assert.True(anyKeyword.Succeeded); Assert.Equal(50, anyKeyword.Value!.Items.Count);
        var missing = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, memberIds: [Guid.NewGuid()]);
        Assert.True(missing.Succeeded); Assert.Empty(missing.Value!.Items);
        Assert.Equal("invalid_board_filter", (await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct,
            memberIds: [fixture.Owner.Id, fixture.Owner.Id])).ErrorCode);
        await store.RemoveBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, now, ct);
        // Raw fixture removal deliberately retains associations: filtering must
        // exclude departed target membership even before command cleanup occurs.
        var departed = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, memberIds: [fixture.Recipient.Id]);
        Assert.True(departed.Succeeded); Assert.Empty(departed.Value!.Items);
        await store.UpsertBoardMemberAsync(fixture.Board.Id, fixture.Recipient.Id, BoardRole.Member, now, ct);
        Assert.True((await work.SetListLifecycleAsync(list.Id, fixture.Owner.Id, WorkItemLifecycleState.Archived, 1, "fixture", ct)).Succeeded);
        var archived = await work.FilterBoardCardsAsync(fixture.Board.Id, fixture.Owner.Id, null, [], "all", cancellationToken: ct, memberIds: [fixture.Recipient.Id]);
        Assert.True(archived.Succeeded); Assert.Empty(archived.Value!.Items);
    }
}
