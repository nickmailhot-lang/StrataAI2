using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
    public async Task Account_deactivation_clears_all_membership_Organizations_and_preserves_other_assignees_history_and_exact_receipts(bool keyed)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var subject = app.CreateClient();
        await RegisterAndLogin(owner); var cookie = await RegisterAndLogin(subject);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var user = (await subject.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var orgs = app.Services.GetRequiredService<IOrganizationService>(); var memberships = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementService>(); var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var identities = app.Services.GetRequiredService<IIdentityStore>(); var now = DateTimeOffset.UtcNow;
        var cards = new List<CardRecord>(); var organizationIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var org = (await orgs.CreateAsync(actor, $"Account assignment cleanup {i}", null, "fixture", ct)).Value!.Organization.Id; organizationIds.Add(org);
            await memberships.AddOrRestoreMemberAsync(org, user, i == 1 ? OrganizationRole.Admin : OrganizationRole.Member, now, ct);
            var board = (await work.CreateBoardAsync(org, actor, "Assigned Board", null, BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
            await store.UpsertBoardMemberAsync(board.Id, user, BoardRole.Member, now, ct);
            var list = await store.CreateListAsync(board.Id, Guid.NewGuid(), "Assigned List", null, now, ct);
            var card = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Assigned Card", null, null, now, ct); cards.Add(card);
            Assert.True((await work.SetCardMemberAsync(card.Id, user, actor, true, 1, "fixture", ct)).Succeeded);
            if (i == 0) Assert.True((await work.SetCardMemberAsync(card.Id, actor, actor, true, 2, "fixture", ct)).Succeeded);
            if (i == 1)
            {
                Assert.True((await work.SetCardLifecycleAsync(card.Id, actor, WorkItemLifecycleState.Archived, 2, "fixture", ct)).Succeeded);
                Assert.True((await work.ArchiveBoardAsync(board.Id, actor, 1, "fixture", ct)).Succeeded);
            }
            if (i == 2) Assert.Equal(OrganizationRemoveMemberResult.Removed, await memberships.RemoveMemberAsync(org, user, now, ct));
        }
        Assert.Equal(organizationIds.Order(), await memberships.ListMembershipOrganizationIdsAsync(user, ct));
        using var copied = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }); copied.DefaultRequestHeaders.Add("Cookie", cookie);
        var key = Guid.NewGuid().ToString(); using var result = await Mutate(copied, HttpMethod.Post, "/me/deactivate", new { }, keyed ? key : null);
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.Equal(AccountStatus.Deactivated, (await identities.FindUserByIdAsync(user, ct))!.Status);
        Assert.Equal(4, (await store.FindCardAsync(cards[0].Id, ct))!.Version);
        Assert.Equal(4, (await store.FindCardAsync(cards[1].Id, ct))!.Version);
        Assert.Equal(3, (await store.FindCardAsync(cards[2].Id, ct))!.Version);
        var remaining = await work.ListCardMembersAsync(cards[0].Id, actor, cancellationToken: ct);
        Assert.True(remaining.Succeeded); Assert.Equal(actor, Assert.Single(remaining.Value!.Items).UserId);
        var noop = await work.SetCardMemberAsync(cards[0].Id, user, actor, false, 4, "fixture", ct);
        Assert.True(noop.Succeeded); Assert.False(noop.Value!.Changed);
        foreach (var card in cards)
            Assert.True((await store.FindBoardMemberAsync(card.BoardId, user, ct))!.Active);
        Assert.False((await memberships.FindMembershipAsync(organizationIds[2], user, ct))!.Active);
        using var retry = await Mutate(copied, HttpMethod.Post, "/me/deactivate", new { }, keyed ? key : null);
        Assert.Equal(keyed ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized, retry.StatusCode);
        Assert.Equal(4, (await store.FindCardAsync(cards[0].Id, ct))!.Version);
        Assert.Equal(4, (await store.FindCardAsync(cards[1].Id, ct))!.Version);
        Assert.Equal(3, (await store.FindCardAsync(cards[2].Id, ct))!.Version);
    }
}
