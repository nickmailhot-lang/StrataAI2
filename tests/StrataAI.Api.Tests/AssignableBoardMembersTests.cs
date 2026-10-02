using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Assignable_directory_respects_verified_email_policy_and_excludes_suspended_pending_accounts(bool requireVerified)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton(
            new IdentityPolicy(true, requireVerified, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30))));
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var identities = app.Services.GetRequiredService<IIdentityStore>(); var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var ids = new List<Guid>();
        foreach (var status in new[] { AccountStatus.Active, AccountStatus.PendingVerification, AccountStatus.Suspended })
        {
            var id = Guid.NewGuid(); ids.Add(id); var now = DateTimeOffset.UtcNow; var email = $"assignment-policy-{id:N}@example.test";
            Assert.True(await identities.TryCreateUserAsync(new(id, email, email.ToUpperInvariant(), "Assignment policy fixture", null, "en", "UTC",
                status, false, "unused-fixture-hash", now, now, 1), null, null, ct));
            await organizations.AddOrRestoreMemberAsync(fixture.Board.OrganizationId, id, OrganizationRole.Member, now, ct);
            await store.UpsertBoardMemberAsync(fixture.Board.Id, id, BoardRole.Member, now, ct);
        }
        var result = await app.Services.GetRequiredService<IWorkManagementService>().ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Owner.Id, cancellationToken: ct);
        Assert.True(result.Succeeded);
        Assert.Equal(!requireVerified, result.Value!.Items.Any(m => m.UserId == ids[0]));
        Assert.DoesNotContain(result.Value.Items, m => m.UserId == ids[1] || m.UserId == ids[2]);
        var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Policy choices", null, DateTimeOffset.UtcNow, ct);
        var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Policy choices", null, null, DateTimeOffset.UtcNow, ct);
        var options = await app.Services.GetRequiredService<IWorkManagementService>().ListCardMemberOptionsAsync(card.Id, fixture.Owner.Id, cancellationToken: ct);
        Assert.True(options.Succeeded); Assert.Equal(!requireVerified, options.Value!.Items.Any(m => m.UserId == ids[0]));
        Assert.DoesNotContain(options.Value.Items, m => m.UserId == ids[1] || m.UserId == ids[2]);
        Assert.All(options.Value.Items, m => Assert.False(m.Assigned));
    }

    [Fact]
    public async Task Assignable_members_require_current_Board_Organization_and_account_membership_without_admin_profile_disclosure()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var fixture = await BoardInvitationFixtureAsync(app, ct);
        var service = app.Services.GetRequiredService<IWorkManagementService>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var first = await service.ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Owner.Id, cancellationToken: ct);
        Assert.True(first.Succeeded); Assert.Equal(new[] { fixture.Owner.Id, fixture.Inviter.Id }.Order(), first.Value!.Items.Select(m => m.UserId).Order());
        Assert.DoesNotContain(first.Value.Items, m => m.UserId == fixture.Recipient.Id);
        Assert.True((await service.SetBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, BoardRole.Member, "fixture", ct)).Succeeded);
        var memberRead = await service.ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Recipient.Id, cancellationToken: ct);
        Assert.True(memberRead.Succeeded); Assert.Equal(3, memberRead.Value!.Items.Count);
        var json = JsonSerializer.SerializeToElement(memberRead.Value.Items[0]);
        Assert.Equal(new[] { "DisplayName", "UserId" }, json.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.True((await service.RemoveBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, "fixture", ct)).Succeeded);
        Assert.DoesNotContain((await service.ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Owner.Id, cancellationToken: ct)).Value!.Items,
            m => m.UserId == fixture.Recipient.Id);
        Assert.True((await service.SetBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, BoardRole.Member, "fixture", ct)).Succeeded);
        Assert.Equal(OrganizationRemoveMemberResult.Removed, await organizations.RemoveMemberAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, DateTimeOffset.UtcNow, ct));
        Assert.DoesNotContain((await service.ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Owner.Id, cancellationToken: ct)).Value!.Items,
            m => m.UserId == fixture.Recipient.Id);
        await organizations.AddOrRestoreMemberAsync(fixture.Board.OrganizationId, fixture.Recipient.Id, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        Assert.True(await identities.DeactivateUserAsync(fixture.Recipient.Id, DateTimeOffset.UtcNow, ct));
        Assert.DoesNotContain((await service.ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Owner.Id, cancellationToken: ct)).Value!.Items,
            m => m.UserId == fixture.Recipient.Id);
        Assert.Equal("board_not_found", (await service.ListAssignableBoardMembersAsync(fixture.Board.Id, Guid.NewGuid(), Guid.Empty, ct)).ErrorCode);
        Assert.Equal("invalid_board_member_cursor", (await service.ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Owner.Id, Guid.Empty, ct)).ErrorCode);
        Assert.True((await service.SetBoardVisibilityAsync(fixture.Board.Id, fixture.Owner.Id, BoardVisibility.Public, 1, "fixture", ct)).Succeeded);
        var visitor = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var email = $"assignment-visitor-{visitor:N}@example.test";
        Assert.True(await identities.TryCreateUserAsync(new(visitor, email, email.ToUpperInvariant(), "Public visitor fixture", null, "en", "UTC",
            AccountStatus.Active, true, "unused-fixture-hash", now, now, 1), null, null, ct));
        Assert.Equal("board_not_found", (await service.ListAssignableBoardMembersAsync(fixture.Board.Id, visitor, cancellationToken: ct)).ErrorCode);
        Assert.True((await service.ArchiveBoardAsync(fixture.Board.Id, fixture.Owner.Id, 2, "fixture", ct)).Succeeded);
        Assert.Equal("board_not_found", (await service.ListAssignableBoardMembersAsync(fixture.Board.Id, fixture.Owner.Id, cancellationToken: ct)).ErrorCode);
    }

    [Fact]
    public async Task Assignable_directory_endpoint_is_authenticated_scoped_and_bounded_without_email_fields()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); await RegisterAndLogin(owner); var board = await TelemetryBoard(owner, ct);
        var me = await owner.GetFromJsonAsync<JsonElement>("/me", ct); var ownerId = me.GetProperty("id").GetGuid();
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var org = (await work.FindBoardAsync(board, ct))!.OrganizationId;
        var organizations = app.Services.GetRequiredService<IOrganizationStore>(); var identities = app.Services.GetRequiredService<IIdentityStore>();
        for (var i = 0; i < 51; i++)
        {
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var email = $"assignable-{id:N}@example.test";
            Assert.True(await identities.TryCreateUserAsync(new(id, email, email.ToUpperInvariant(), $"Member {i}", null, "en", "UTC",
                AccountStatus.Active, true, "unused-fixture-hash", now, now, 1), null, null, ct));
            await organizations.AddOrRestoreMemberAsync(org, id, OrganizationRole.Member, now, ct);
            await work.UpsertBoardMemberAsync(board, id, BoardRole.Member, now, ct);
        }
        var first = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/assignable-members", ct);
        Assert.Equal(org, first.GetProperty("organizationId").GetGuid()); Assert.Equal(board, first.GetProperty("boardId").GetGuid());
        Assert.Equal(50, first.GetProperty("items").GetArrayLength()); var cursor = first.GetProperty("nextCursor").GetGuid();
        Assert.Equal(cursor, first.GetProperty("items")[49].GetProperty("userId").GetGuid());
        var second = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/assignable-members?after={cursor}", ct);
        Assert.Equal(2, second.GetProperty("items").GetArrayLength()); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var members = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(52, members.Select(m => m.GetProperty("userId").GetGuid()).Distinct().Count());
        Assert.Contains(members, m => m.GetProperty("userId").GetGuid() == ownerId);
        Assert.All(members, m => Assert.Equal(new[] { "displayName", "userId" }, m.EnumerateObject().Select(p => p.Name).Order().ToArray()));
        using var invalid = await owner.GetAsync($"/boards/{board}/assignable-members?after=invalid", ct); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var anonymous = app.CreateClient(); using var denied = await anonymous.GetAsync($"/boards/{board}/assignable-members", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
}
