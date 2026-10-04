using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Search_content_matches_description_label_and_eligible_member_with_ANY_ALL_and_archive_scope()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var now = DateTimeOffset.UtcNow;
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Search", null, now, ct);
        var first = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "First", "Description needle", null, now, ct);
        var second = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Other text", null, null, now, ct);
        var label = await store.CreateLabelAsync(fixture.Board.Id, Guid.NewGuid(), "Priority", "#112233", now, ct);
        Assert.NotNull(await store.SetCardLabelAsync(first.Id, label.Id, true, 1, now, ct));
        Assert.NotNull(await store.SetCardMemberAsync(first.Id, fixture.Owner.Id, fixture.Owner.Id, true, 2, now, ct));
        var binding = new GlobalSearchBinding(fixture.Owner.Id, "NEEDLE", "prior", "Board command", true, GlobalSearchLifecycleScope.Active);
        var service = app.Services.GetRequiredService<IWorkManagementService>();
        var admitted = await service.SearchBoardAsync(fixture.Board.Id, binding, cancellationToken: ct);
        Assert.True(admitted.Succeeded);
        var document = Assert.Single(admitted.Value!.Items);
        Assert.Equal("CARD", document.SourceKind); Assert.Equal(first.Id, document.Card.Id);
        Assert.Equal(fixture.Board.Name, document.BoardName); Assert.Equal(list.Name, document.ListName);
        Assert.Equal(label.Id, Assert.Single(document.Labels).Id);
        Assert.Equal(fixture.Owner.Id, Assert.Single(document.Members).UserId);
        Assert.False(document.HasMoreLabels); Assert.False(document.HasMoreMembers);
        Assert.Null(admitted.Value.NextCard);
        Assert.Equal("invalid_search", (await service.SearchBoardAsync(fixture.Board.Id,
            binding with { Keyword = new string('x', 161) }, cancellationToken: ct)).ErrorCode);
        Assert.Equal("board_not_found", (await service.SearchBoardAsync(fixture.Board.Id,
            binding with { ActorId = Guid.NewGuid(), Keyword = new string('x', 161) }, cancellationToken: ct)).ErrorCode);
        Assert.Equal(first.Id, Assert.Single(await store.SearchBoardCardsAsync(fixture.Board.Id, binding, false, null, ct)).Id);
        Assert.Empty(await store.SearchBoardCardsAsync(fixture.Board.Id, binding with { Keyword = "Other" }, false, null, ct));
        Assert.Equal(new[] { first.Id, second.Id }.Order(), (await store.SearchBoardCardsAsync(fixture.Board.Id,
            binding with { Keyword = "Other", MatchAll = false }, false, null, ct)).Select(c => c.Id));
        Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().SetListLifecycleAsync(
            list.Id, fixture.Owner.Id, WorkItemLifecycleState.Archived, 1, "search-scope", ct)).Succeeded);
        Assert.Empty(await store.SearchBoardCardsAsync(fixture.Board.Id, binding, false, null, ct));
        Assert.Empty((await service.SearchBoardAsync(fixture.Board.Id, binding, cancellationToken: ct)).Value!.Items);
        var archivedRead = await service.SearchBoardAsync(fixture.Board.Id,
            binding with { Scope = GlobalSearchLifecycleScope.Archived }, cancellationToken: ct);
        Assert.True(archivedRead.Succeeded); Assert.Equal(first.Id, Assert.Single(archivedRead.Value!.Items).Card.Id);
        Assert.Equal(first.Id, Assert.Single(await store.SearchBoardCardsAsync(fixture.Board.Id,
            binding with { Scope = GlobalSearchLifecycleScope.Archived }, false, null, ct)).Id);
        Assert.Empty(await store.SearchBoardCardsAsync(Guid.NewGuid(), binding, false, null, ct));
    }

    [Fact]
    public async Task Search_routing_pages_seek_without_duplicates_and_exclude_other_actors_and_tenants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var boards = app.Services.GetRequiredService<IWorkManagementStore>();
        var now = DateTimeOffset.UtcNow;
        for (var n = 0; n < 52; n++)
        {
            await organizations.CreateOrganizationAsync(fixture.Owner.Id, Guid.NewGuid(), $"Tenant {n}", null, now, ct);
            await boards.CreateBoardAsync(fixture.Board.OrganizationId, fixture.Owner.Id, Guid.NewGuid(), $"Board {n}", null,
                BoardVisibility.Private, "COLOR", "#ffffff", now, ct);
        }
        var routes = await organizations.ListMembershipOrganizationIdsPageAsync(fixture.Owner.Id, null, ct);
        Assert.Equal(51, routes.Count);
        var routeTail = await organizations.ListMembershipOrganizationIdsPageAsync(fixture.Owner.Id, routes[49], ct);
        var expectedRoutes = await organizations.ListMembershipOrganizationIdsAsync(fixture.Owner.Id, ct);
        Assert.Equal(expectedRoutes.Order(), routes.Take(50).Concat(routeTail));
        Assert.Empty(await organizations.ListMembershipOrganizationIdsPageAsync(Guid.NewGuid(), null, ct));
        var first = await boards.ListVisibleBoardsPageAsync(fixture.Board.OrganizationId, fixture.Owner.Id, false, null, ct);
        Assert.Equal(51, first.Count);
        var tail = await boards.ListVisibleBoardsPageAsync(fixture.Board.OrganizationId, fixture.Owner.Id, false, first[49].Id, ct);
        var expectedBoards = await boards.ListVisibleBoardsAsync(fixture.Board.OrganizationId, fixture.Owner.Id, false, ct);
        var ids = first.Take(50).Concat(tail).Select(b => b.Id).ToArray();
        Assert.Equal(expectedBoards.Select(b => b.Id).Order(), ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Empty(await boards.ListVisibleBoardsPageAsync(fixture.Board.OrganizationId, Guid.NewGuid(), false, null, ct));
        Assert.Empty(await boards.ListVisibleBoardsPageAsync(Guid.NewGuid(), fixture.Owner.Id, true, null, ct));
    }
}
