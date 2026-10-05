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
    private sealed class SearchSourceActorFixture : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    // Demo store/transaction parity with synthetic actor admission. No source
    // HTTP producer or current browser session/replay proof is claimed here.
    [Fact]
    public async Task Private_search_sources_require_owned_actor_preserve_original_identity_and_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization, SearchSourceActorFixture>());
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var other = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var sources = app.Services.GetRequiredService<ISearchInteractionEventStore>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var at = DateTimeOffset.UtcNow;
        var source = SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), actor, at);
        var unowned = await Assert.ThrowsAsync<InvalidOperationException>(() => sources.AppendAsync(source, ct));
        Assert.Equal("Search interaction requires the owning identity subject transaction.", unowned.Message);
        async Task<IdentityOperation<bool>> Append(SearchInteractionEvent value) => await unit.ExecuteAsync(value.ActorId, async () =>
        { await sources.AppendAsync(value, ct); return IdentityOperation<bool>.Success(true); }, ct);
        Assert.True((await Append(source)).Succeeded);
        Assert.True((await Append(source)).Succeeded);
        var changed = await Assert.ThrowsAsync<InvalidOperationException>(() => Append(SearchInteractionEvent.SearchExecuted(source.EventId, actor, at.AddSeconds(1))));
        Assert.Equal("Search interaction unavailable.", changed.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync<bool>(actor, async () =>
        { await sources.AppendAsync(SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), other, at), ct); return IdentityOperation<bool>.Success(true); }, ct));
        var rollbackId = Guid.NewGuid();
        var refused = await unit.ExecuteAsync<bool>(actor, async () =>
        {
            await sources.AppendAsync(SearchInteractionEvent.SearchExecuted(rollbackId, actor, at), ct);
            return IdentityOperation<bool>.Failure("declared_late_refusal");
        }, ct);
        Assert.Equal("declared_late_refusal", refused.ErrorCode);
        // Different source clock can become the original only if rollback
        // actually removed the previously appended immutable identity.
        Assert.True((await Append(SearchInteractionEvent.SearchExecuted(rollbackId, actor, at.AddSeconds(2)))).Succeeded);

        var organizations = app.Services.GetRequiredService<IOrganizationService>();
        var organizationStore = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var workStore = app.Services.GetRequiredService<IWorkManagementStore>();
        var organization = (await organizations.CreateAsync(actor, "Source scope", null, "fixture", ct)).Value!.Organization.Id;
        await organizationStore.AddOrRestoreMemberAsync(organization, other, OrganizationRole.Member, at, ct);
        var board = (await work.CreateBoardAsync(organization, actor, "Private source", null, BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
        var boardSource = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), other, organization, board.Id, at);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(boardSource));
        await workStore.UpsertBoardMemberAsync(board.Id, other, BoardRole.Member, at, ct);
        Assert.True((await Append(boardSource)).Succeeded);
        await workStore.RemoveBoardMemberAsync(board.Id, other, at.AddSeconds(1), ct);
        // Duplicate identity is freshly admitted too; withdrawing a grant must
        // not be bypassed by an earlier successful append.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(boardSource));
        Assert.True((await Append(SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), other, at))).Succeeded);
    }
}
