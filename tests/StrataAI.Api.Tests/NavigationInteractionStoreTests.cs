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
    [Fact]
    public async Task PRD_01_Demo_navigation_store_checks_owned_actor_current_private_scope_revision_and_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization, SearchSourceActorFixture>());
        using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var other = (await outsider.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var orgs = app.Services.GetRequiredService<IOrganizationService>();
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<INavigationInteractionEventStore>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var org = (await orgs.CreateAsync(actor, "Navigation scope", null, "fixture", ct)).Value!.Organization.Id;
        var board = (await work.CreateBoardAsync(org, actor, "Private navigation", null, BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
        var at = DateTimeOffset.UtcNow;
        var source = NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, org, board.Id, board.Version, at);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAuthorizedAsync(source, ct));
        async Task<IdentityOperation<bool>> Append(NavigationInteractionEvent candidate) => await unit.ExecuteAsync(candidate.ActorId,
            async () => IdentityOperation<bool>.Success(await store.AppendAuthorizedAsync(candidate, ct)), ct);
        var receipts = app.Services.GetRequiredService<INavigationInteractionReplayStore>();
        var request = Guid.NewGuid(); var digest = new string('a', 64);
        async Task<IdentityOperation<NavigationInteractionEvent>> Retry(Guid key, string fingerprint, NavigationInteractionEvent candidate) =>
            await unit.ExecuteAsync(actor, async () => {
                var value = await receipts.AppendOrReplayAuthorizedAsync(key, fingerprint, candidate, ct);
                return value is null ? IdentityOperation<NavigationInteractionEvent>.Failure("navigation_unavailable")
                    : IdentityOperation<NavigationInteractionEvent>.Success(value);
            }, ct);
        var original = NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, org, board.Id, board.Version, at);
        Assert.Equal(original, (await Retry(request, digest, original)).Value);
        var duplicate = NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, org, board.Id, board.Version, at.AddSeconds(1));
        Assert.Equal(original, (await Retry(request, digest, duplicate)).Value);
        Assert.False((await Retry(request, new string('b', 64), duplicate)).Succeeded);
        var rollbackRequest = Guid.NewGuid();
        Assert.False((await unit.ExecuteAsync<bool>(actor, async () => {
            Assert.NotNull(await receipts.AppendOrReplayAuthorizedAsync(rollbackRequest, digest, duplicate, ct));
            return IdentityOperation<bool>.Failure("navigation_receipt_rollback");
        }, ct)).Succeeded);
        var replacement = NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, org, board.Id, board.Version, at.AddSeconds(2));
        Assert.Equal(replacement, (await Retry(rollbackRequest, digest, replacement)).Value);
        Assert.True((await Append(NavigationInteractionEvent.ApplicationContextChanged(Guid.NewGuid(), actor, org, at))).Value);
        Assert.True((await Append(source)).Value);
        Assert.True((await Append(source)).Value);
        Assert.False((await Append(NavigationInteractionEvent.BoardOpened(source.EventId, actor, org, board.Id, board.Version, at.AddSeconds(1)))).Value);
        Assert.False((await Append(NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, org, board.Id, board.Version + 1, at))).Value);
        Assert.False((await Append(NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), other, org, board.Id, board.Version, at))).Value);
        Assert.False((await Append(NavigationInteractionEvent.ApplicationContextChanged(Guid.NewGuid(), other, org, at))).Value);
        var rollbackId = Guid.NewGuid();
        var refused = await unit.ExecuteAsync<bool>(actor, async () => {
            Assert.True(await store.AppendAuthorizedAsync(NavigationInteractionEvent.BoardOpened(rollbackId, actor, org, board.Id, board.Version, at), ct));
            return IdentityOperation<bool>.Failure("late_navigation_refusal");
        }, ct);
        Assert.Equal("late_navigation_refusal", refused.ErrorCode);
        // A different immutable clock can become original only after rollback.
        Assert.True((await Append(NavigationInteractionEvent.BoardOpened(rollbackId, actor, org, board.Id, board.Version, at.AddSeconds(2)))).Value);
        var list = (await work.CreateListAsync(board.Id, actor, "Navigation List", null, "fixture", ct)).Value!;
        var card = (await work.CreateCardAsync(list.Id, actor, "Navigation Card", null, null, "fixture", ct)).Value!;
        var otherBoard = (await work.CreateBoardAsync(org, actor, "Other navigation Board", null, BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
        Assert.True((await Append(NavigationInteractionEvent.CardOpened(Guid.NewGuid(), actor, org, board.Id, card.Id, card.Version, at))).Value);
        Assert.False((await Append(NavigationInteractionEvent.CardOpened(Guid.NewGuid(), actor, org, otherBoard.Id, card.Id, card.Version, at))).Value);
        Assert.False((await Append(NavigationInteractionEvent.CardOpened(Guid.NewGuid(), actor, org, board.Id, card.Id, card.Version + 1, at))).Value);
        Assert.Equal(card.Version, (await app.Services.GetRequiredService<IWorkManagementStore>().FindCardAsync(card.Id, ct))!.Version);
        Assert.Equal(board.Version, (await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardAsync(board.Id, ct))!.Version);
    }
}
