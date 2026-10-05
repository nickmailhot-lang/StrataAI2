using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class SearchSourceActorFixture : ICommandActorAuthorization
    {
        private int _checks;
        private int? _refuseAt;
        public void RefuseAt(int? check) { _checks = 0; _refuseAt = check; }
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
            => Task.FromResult(++_checks != _refuseAt);
    }

    private sealed class ObservedSearchSource(ISearchInteractionEventStore inner) : ISearchInteractionEventStore
    {
        public SearchInteractionEvent? Last { get; private set; }
        public async Task AppendAsync(SearchInteractionEvent source, CancellationToken cancellationToken = default)
        { await inner.AppendAsync(source, cancellationToken); Last = source; }
    }

    [Fact]
    public async Task Search_producer_final_session_refusal_rolls_back_original_and_returns_no_acknowledgment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization, SearchSourceActorFixture>());
        using var client = app.CreateClient(); await RegisterAndLogin(client);
        var actor = (await client.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var sources = app.Services.GetRequiredService<ISearchInteractionEventStore>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var proof = (SearchSourceActorFixture)app.Services.GetRequiredService<ICommandActorAuthorization>();
        var observed = new ObservedSearchSource(sources);
        var producer = new SearchInteractionEventProducer(observed, unit, proof, app.Services.GetRequiredService<IClock>());
        proof.RefuseAt(2); // owning admission succeeds; final original-session proof fails
        var refused = await producer.SearchExecutedAsync(actor, ct);
        Assert.Equal("session_unavailable", refused.ErrorCode); Assert.Null(refused.Value);
        Assert.NotNull(observed.Last);
        proof.RefuseAt(null);
        var replacement = SearchInteractionEvent.SearchExecuted(observed.Last.EventId, actor, observed.Last.CreatedAt.AddSeconds(1));
        var appended = await unit.ExecuteAsync<bool>(actor, async () =>
        { await sources.AppendAsync(replacement, ct); return IdentityOperation<bool>.Success(true); }, ct);
        Assert.True(appended.Succeeded); // changed original proves the refused source did not survive
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
        var proof = (SearchSourceActorFixture)app.Services.GetRequiredService<ICommandActorAuthorization>();
        var lateId = Guid.NewGuid();
        proof.RefuseAt(3); // identity admission, Work pre-proof, Work final proof
        var caught = await unit.ExecuteAsync<bool>(other, async () =>
        {
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => sources.AppendAsync(
                SearchInteractionEvent.BoardFilterChanged(lateId, other, organization, board.Id, at), ct));
            Assert.Equal("Search interaction unavailable.", refusal.Message);
            return IdentityOperation<bool>.Success(true);
        }, ct);
        Assert.True(caught.Succeeded);
        proof.RefuseAt(null);
        Assert.True((await Append(SearchInteractionEvent.BoardFilterChanged(lateId, other, organization, board.Id, at.AddSeconds(2)))).Succeeded);
        await workStore.RemoveBoardMemberAsync(board.Id, other, at.AddSeconds(1), ct);
        // Duplicate identity is freshly admitted too; withdrawing a grant must
        // not be bypassed by an earlier successful append.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(boardSource));
        Assert.True((await Append(SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), other, at))).Succeeded);
    }
}
