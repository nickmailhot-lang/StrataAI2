using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-11-VS-01-T-02 / UX-CARD-02: zero is a canonical, version-bound
    // producer result; the browser must never infer it from missing metadata.
    [Theory]
    [InlineData(BoardRole.Member)]
    [InlineData(null)]
    [InlineData(BoardRole.Admin)]
    public async Task Card_member_previews_include_explicit_empty_visible_Cards_for_admitted_roles(BoardRole? role)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var now = DateTimeOffset.UtcNow;
        if (role is { } boardRole)
            Assert.True((await work.SetBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, boardRole, "fixture", ct)).Succeeded);
        else
        {
            // A read-only Organization viewer has no explicit Board grant.
            var current = await work.GetBoardAsync(fixture.Board.Id, fixture.Owner.Id, ct);
            Assert.True((await work.SetBoardVisibilityAsync(fixture.Board.Id, fixture.Owner.Id, BoardVisibility.Organization, current.Value!.Board.Version, "fixture", ct)).Succeeded);
        }
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Preview list", null, now, ct);
        var empty = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "No assignees", null, null, now, ct);
        var assigned = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Assigned", null, null, now, ct);
        var archived = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Archived", null, null, now, ct);
        Assert.True((await work.SetCardMemberAsync(assigned.Id, fixture.Owner.Id, fixture.Owner.Id, true, 1, "fixture", ct)).Succeeded);
        Assert.True((await work.SetCardLifecycleAsync(archived.Id, fixture.Owner.Id, WorkItemLifecycleState.Archived, 1, "fixture", ct)).Succeeded);
        var snapshot = await work.GetBoardAsync(fixture.Board.Id, fixture.Recipient.Id, ct);
        Assert.True(snapshot.Succeeded); Assert.NotNull(snapshot.Value!.CardMembers);
        Assert.Equal(2, snapshot.Value.CardMembers.Count);
        var zero = snapshot.Value.CardMembers[empty.Id]; Assert.Empty(zero.Items);
        Assert.Equal(0, zero.Total); Assert.Equal(empty.Version, zero.CardVersion);
        var one = snapshot.Value.CardMembers[assigned.Id]; Assert.Equal(1, one.Total);
        Assert.Equal(fixture.Owner.Id, Assert.Single(one.Items).UserId); Assert.Equal(2, one.CardVersion);
        Assert.DoesNotContain(archived.Id, snapshot.Value.CardMembers.Keys);
        var detailed = await work.ListCardMembersAsync(empty.Id, fixture.Recipient.Id, cancellationToken: ct);
        Assert.True(detailed.Succeeded); Assert.Empty(detailed.Value!.Items);
        Assert.Equal(zero.CardVersion, detailed.Value.CardVersion);
        Assert.Equal(role.HasValue, detailed.Value.CanEdit);
        if (role.HasValue)
            Assert.True((await work.RemoveBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, "fixture", ct)).Succeeded);
        else
            Assert.True((await app.Services.GetRequiredService<IOrganizationService>().RemoveMemberAsync(fixture.Board.OrganizationId, fixture.Owner.Id, fixture.Recipient.Id, "fixture", ct)).Succeeded);
        var withdrawn = await work.GetBoardAsync(fixture.Board.Id, fixture.Recipient.Id, ct);
        Assert.False(withdrawn.Succeeded); Assert.Null(withdrawn.Value);
    }

    [Fact]
    public async Task Card_member_previews_remain_absent_for_anonymous_and_foreign_Organization_public_visitors()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct); var foreign = await BoardInvitationFixtureAsync(app, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>(); var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Public preview", null, DateTimeOffset.UtcNow, ct);
        var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Visible Card", null, null, DateTimeOffset.UtcNow, ct);
        Assert.False((await work.GetBoardAsync(fixture.Board.Id, foreign.Owner.Id, ct)).Succeeded);
        var before = await work.GetBoardAsync(fixture.Board.Id, fixture.Owner.Id, ct);
        Assert.True((await work.SetBoardVisibilityAsync(fixture.Board.Id, fixture.Owner.Id, BoardVisibility.Public, before.Value!.Board.Version, "fixture", ct)).Succeeded);
        foreach (var actor in new Guid?[] { null, foreign.Owner.Id })
        {
            var visible = await work.GetBoardAsync(fixture.Board.Id, actor, ct);
            Assert.True(visible.Succeeded); Assert.Null(visible.Value!.CardMembers);
            Assert.Equal(card.Id, Assert.Single(Assert.Single(visible.Value.Lists).Cards).Id);
        }
        var admitted = await work.GetBoardAsync(fixture.Board.Id, fixture.Owner.Id, ct);
        Assert.Empty(admitted.Value!.CardMembers![card.Id].Items);
    }

    [Fact]
    public async Task Card_member_previews_follow_last_assignment_removal_without_inventing_a_revision()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct); var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var events = app.Services.GetRequiredService<IWorkEventReader>();
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Assignment changes", null, DateTimeOffset.UtcNow, ct);
        var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Current assignment", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await work.SetCardMemberAsync(card.Id, fixture.Owner.Id, fixture.Owner.Id, true, 1, "fixture", ct)).Succeeded);
        Assert.True((await work.SetCardMemberAsync(card.Id, fixture.Owner.Id, fixture.Owner.Id, false, 2, "fixture", ct)).Succeeded);
        var before = await events.ReadAsync(fixture.Board.OrganizationId, fixture.Board.Id, 0, 100, ct);
        var snapshot = await work.GetBoardAsync(fixture.Board.Id, fixture.Owner.Id, ct);
        Assert.True(snapshot.Succeeded); var preview = snapshot.Value!.CardMembers![card.Id];
        Assert.Empty(preview.Items); Assert.Equal(0, preview.Total); Assert.Equal(3, preview.CardVersion);
        Assert.Equal(3, (await store.FindCardAsync(card.Id, ct))!.Version);
        var after = await events.ReadAsync(fixture.Board.OrganizationId, fixture.Board.Id, 0, 100, ct);
        Assert.Equal(before.Events.Select(x => x.Event.EventId), after.Events.Select(x => x.Event.EventId));
    }

    [Fact]
    public async Task Card_member_previews_empty_state_retains_current_account_eligibility_filtering()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct); var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var identities = app.Services.GetRequiredService<IIdentityStore>();
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Eligibility", null, DateTimeOffset.UtcNow, ct);
        var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Former assignee", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await work.SetCardMemberAsync(card.Id, fixture.Inviter.Id, fixture.Owner.Id, true, 1, "fixture", ct)).Succeeded);
        Assert.True(await identities.DeactivateUserAsync(fixture.Inviter.Id, DateTimeOffset.UtcNow, ct));
        var snapshot = await work.GetBoardAsync(fixture.Board.Id, fixture.Owner.Id, ct);
        Assert.True(snapshot.Succeeded); var preview = snapshot.Value!.CardMembers![card.Id];
        Assert.Empty(preview.Items); Assert.Equal(0, preview.Total); Assert.Equal(2, preview.CardVersion);
        var detailed = await work.ListCardMembersAsync(card.Id, fixture.Owner.Id, cancellationToken: ct);
        Assert.True(detailed.Succeeded); Assert.Empty(detailed.Value!.Items);
        Assert.Equal(preview.CardVersion, detailed.Value.CardVersion);
    }
}
