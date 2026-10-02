using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Organization_removal_and_leave_clear_assignments_across_Boards_preserving_other_Organizations_and_assignees(bool leave)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct); var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var organizations = app.Services.GetRequiredService<IOrganizationService>();
        var memberships = app.Services.GetRequiredService<IOrganizationStore>(); var now = DateTimeOffset.UtcNow;
        var second = (await work.CreateBoardAsync(fixture.Board.OrganizationId, fixture.Owner.Id, "Second assigned Board", null,
            BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
        var otherOrg = (await organizations.CreateAsync(fixture.Owner.Id, "Other assignment Organization", null, "fixture", ct)).Value!.Organization.Id;
        await memberships.AddOrRestoreMemberAsync(otherOrg, fixture.Recipient.Id, OrganizationRole.Member, now, ct);
        var foreign = (await work.CreateBoardAsync(otherOrg, fixture.Owner.Id, "Foreign assignment Board", null,
            BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
        async Task<CardRecord> AssignedCard(BoardRecord board)
        {
            await store.UpsertBoardMemberAsync(board.Id, fixture.Recipient.Id, BoardRole.Member, now, ct);
            var list = await store.CreateListAsync(board.Id, Guid.NewGuid(), "Assignment cleanup List", null, now, ct);
            var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Assignment cleanup Card", null, null, now, ct);
            Assert.True((await work.SetCardMemberAsync(card.Id, fixture.Recipient.Id, fixture.Owner.Id, true, 1, "fixture", ct)).Succeeded);
            return card;
        }
        var active = await AssignedCard(fixture.Board); var archived = await AssignedCard(second); var retained = await AssignedCard(foreign);
        Assert.True((await work.SetCardMemberAsync(active.Id, fixture.Owner.Id, fixture.Owner.Id, true, 2, "fixture", ct)).Succeeded);
        Assert.True((await work.SetCardLifecycleAsync(archived.Id, fixture.Owner.Id, WorkItemLifecycleState.Archived, 2, "fixture", ct)).Succeeded);
        var result = leave ? await organizations.LeaveAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, "fixture", ct)
            : await organizations.RemoveMemberAsync(fixture.Board.OrganizationId, fixture.Owner.Id, fixture.Recipient.Id, "fixture", ct);
        Assert.True(result.Succeeded); Assert.Equal(4, (await store.FindCardAsync(active.Id, ct))!.Version);
        Assert.Equal(4, (await store.FindCardAsync(archived.Id, ct))!.Version); Assert.Equal(2, (await store.FindCardAsync(retained.Id, ct))!.Version);
        var membership = await memberships.FindMembershipAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, ct);
        Assert.NotNull(membership); Assert.False(membership.Active);
        Assert.NotNull(await app.Services.GetRequiredService<IIdentityStore>().FindUserByIdAsync(fixture.Recipient.Id, ct));
        Assert.True((await work.SetCardMemberAsync(retained.Id, fixture.Recipient.Id, fixture.Owner.Id, true, 2, "fixture", ct)).Value is { Changed: false });
        Assert.True((await work.SetCardMemberAsync(active.Id, fixture.Owner.Id, fixture.Owner.Id, true, 4, "fixture", ct)).Value is { Changed: false });
        await memberships.AddOrRestoreMemberAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, OrganizationRole.Member, now, ct);
        var removed = await work.SetCardMemberAsync(active.Id, fixture.Recipient.Id, fixture.Owner.Id, false, 4, "fixture", ct);
        Assert.True(removed.Succeeded); Assert.False(removed.Value!.Changed);
    }
}
