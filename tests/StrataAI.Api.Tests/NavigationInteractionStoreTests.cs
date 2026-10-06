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
        Assert.Equal(board.Version, (await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardAsync(board.Id, ct))!.Version);
    }
}
