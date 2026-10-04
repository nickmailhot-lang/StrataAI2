using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
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
