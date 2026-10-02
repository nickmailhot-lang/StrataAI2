using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Card_assignee_pages_seek_past_50_and_filter_current_account_eligibility_before_paging()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct); var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var identities = app.Services.GetRequiredService<IIdentityStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>(); var now = DateTimeOffset.UtcNow;
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Paged assignees", null, now, ct);
        var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Paged assignee Card", null, null, now, ct);
        var ids = new List<Guid>();
        for (var i = 0; i < 53; i++)
        {
            var id = Guid.NewGuid(); ids.Add(id); var email = $"assignee-page-{id:N}@example.test";
            Assert.True(await identities.TryCreateUserAsync(new(id, email, email.ToUpperInvariant(), $"Assignee {i}", null, "en", "UTC",
                AccountStatus.Active, true, "unused-fixture-hash", now, now, 1), null, null, ct));
            await organizations.AddOrRestoreMemberAsync(fixture.Board.OrganizationId, id, OrganizationRole.Member, now, ct);
            await store.UpsertBoardMemberAsync(fixture.Board.Id, id, BoardRole.Member, now, ct);
            Assert.True((await work.SetCardMemberAsync(card.Id, id, fixture.Owner.Id, true, i + 1, "fixture", ct)).Succeeded);
        }
        // Model a separately changed account. Ineligible associations cannot be
        // surfaced, even before account-deactivation cleanup is implemented.
        Assert.True(await identities.DeactivateUserAsync(ids[0], now, ct));
        var first = await work.ListCardMembersAsync(card.Id, fixture.Owner.Id, cancellationToken: ct);
        Assert.True(first.Succeeded); Assert.Equal(54, first.Value!.CardVersion); Assert.Equal(50, first.Value.Items.Count);
        Assert.Equal(first.Value.Items[^1].UserId, first.Value.NextCursor);
        var second = await work.ListCardMembersAsync(card.Id, fixture.Owner.Id, first.Value.NextCursor, ct);
        Assert.True(second.Succeeded); Assert.Equal(2, second.Value!.Items.Count); Assert.Null(second.Value.NextCursor);
        var items = first.Value.Items.Concat(second.Value.Items).ToArray(); Assert.Equal(52, items.Select(i => i.UserId).Distinct().Count());
        Assert.DoesNotContain(items, i => i.UserId == ids[0]); Assert.All(items, i => Assert.Equal(fixture.Owner.Id, i.AssignedBy));
        Assert.Equal(items.Select(i => i.UserId).Order(), items.Select(i => i.UserId));
        Assert.Equal("invalid_board_member_cursor", (await work.ListCardMembersAsync(card.Id, fixture.Owner.Id, Guid.Empty, ct)).ErrorCode);
        var choices = await work.ListCardMemberOptionsAsync(card.Id, fixture.Owner.Id, cancellationToken: ct);
        Assert.True(choices.Succeeded); Assert.Equal(54, choices.Value!.CardVersion); Assert.Equal(50, choices.Value.Items.Count);
        var more = await work.ListCardMemberOptionsAsync(card.Id, fixture.Owner.Id, choices.Value.NextCursor, ct);
        Assert.True(more.Succeeded); Assert.Equal(4, more.Value!.Items.Count); Assert.Null(more.Value.NextCursor);
        var options = choices.Value.Items.Concat(more.Value.Items).ToArray();
        Assert.Equal(54, options.Select(i => i.UserId).Distinct().Count());
        Assert.DoesNotContain(options, i => i.UserId == ids[0] || i.UserId == fixture.Recipient.Id);
        Assert.Equal(ids.Skip(1).Order(), options.Where(i => i.Assigned).Select(i => i.UserId).Order());
        Assert.Equal(new[] { fixture.Owner.Id, fixture.Inviter.Id }.Order(), options.Where(i => !i.Assigned).Select(i => i.UserId).Order());
        Assert.Equal("invalid_board_member_cursor", (await work.ListCardMemberOptionsAsync(card.Id, fixture.Owner.Id, Guid.Empty, ct)).ErrorCode);
    }
}
