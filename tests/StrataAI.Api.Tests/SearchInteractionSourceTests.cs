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
        public Func<int, Task>? OnProof { get; set; }
        public void RefuseAt(int? check) { _checks = 0; _refuseAt = check; }
        public async Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        {
            var check = ++_checks;
            if (OnProof is not null) await OnProof(check);
            return check != _refuseAt;
        }
    }

    private sealed class ObservedSearchSource(ISearchInteractionEventStore inner) : ISearchInteractionEventStore
    {
        public SearchInteractionEvent? Last { get; private set; }
        public async Task AppendAsync(SearchInteractionEvent source, CancellationToken cancellationToken = default)
        { await inner.AppendAsync(source, cancellationToken); Last = source; }
    }

    [Fact]
    public async Task Board_filter_retry_store_preserves_original_and_rolls_back_receipt_with_source()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services =>
        { services.AddSingleton<ICommandActorAuthorization, SearchSourceActorFixture>(); services.AddSingleton<IClock>(clock); });
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var other = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var org = (await app.Services.GetRequiredService<IOrganizationService>().CreateAsync(actor, "Filter retry", null, "fixture", ct)).Value!.Organization.Id;
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, other, OrganizationRole.Member, clock.UtcNow, ct);
        var board = (await app.Services.GetRequiredService<IWorkManagementService>().CreateBoardAsync(org, actor, "Retry Board", null, BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        await work.UpsertBoardMemberAsync(board.Id, other, BoardRole.Member, clock.UtcNow, ct);
        var replays = app.Services.GetRequiredService<IBoardFilterInteractionReplayStore>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var request = Guid.NewGuid(); var digest = new string('a', 64);
        var first = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), other, org, board.Id, clock.UtcNow);
        Task<IdentityOperation<SearchInteractionEvent>> Append(Guid key, string fingerprint, SearchInteractionEvent candidate) =>
            unit.ExecuteAsync(candidate.ActorId, async () => IdentityOperation<SearchInteractionEvent>.Success(
                await replays.AppendOrReplayAsync(key, fingerprint, candidate, ct)), ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replays.AppendOrReplayAsync(request, digest, first, ct));
        Assert.Equal(first, (await Append(request, digest, first)).Value);
        var retry = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), other, org, board.Id, clock.UtcNow.AddSeconds(1));
        Assert.Equal(first, (await Append(request, digest, retry)).Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(request, new string('b', 64), retry));
        var lateKey = Guid.NewGuid();
        var refused = await unit.ExecuteAsync<bool>(other, async () =>
        {
            await replays.AppendOrReplayAsync(lateKey, digest, retry, ct);
            return IdentityOperation<bool>.Failure("retry_late_refusal");
        }, ct);
        Assert.Equal("retry_late_refusal", refused.ErrorCode);
        var replacement = SearchInteractionEvent.BoardFilterChanged(retry.EventId, other, org, board.Id, retry.CreatedAt.AddSeconds(1));
        Assert.Equal(replacement, (await Append(lateKey, digest, replacement)).Value);
        await work.RemoveBoardMemberAsync(board.Id, other, clock.UtcNow, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(request, digest, retry));
        await work.UpsertBoardMemberAsync(board.Id, other, BoardRole.Member, clock.UtcNow, ct);
        clock.UtcNow = clock.UtcNow.AddHours(24);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(request, digest, retry));
        // A new request can remove expired receipts while leaving their source
        // originals immutable; failed/caught inner receipt writes never survive.
        var fresh = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), other, org, board.Id, clock.UtcNow);
        Assert.Equal(fresh, (await Append(Guid.NewGuid(), digest, fresh)).Value);
        var sources = app.Services.GetRequiredService<ISearchInteractionEventStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync<bool>(other, async () =>
        {
            await sources.AppendAsync(SearchInteractionEvent.BoardFilterChanged(first.EventId, other, org, board.Id, first.CreatedAt.AddSeconds(1)), ct);
            return IdentityOperation<bool>.Success(true);
        }, ct));
        Assert.Equal(board.Version, (await work.FindBoardAsync(board.Id, ct))!.Version);
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

    [Fact]
    public async Task Board_filter_producer_requires_actual_private_grant_preserves_work_version_and_rolls_back_final_session_refusal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization, SearchSourceActorFixture>());
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var other = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var organization = (await app.Services.GetRequiredService<IOrganizationService>().CreateAsync(actor, "Filter producer", null, "fixture", ct)).Value!.Organization.Id;
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(organization, other, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var board = (await app.Services.GetRequiredService<IWorkManagementService>().CreateBoardAsync(organization, actor, "Filter source", null, BoardVisibility.Private, "COLOR", null, "fixture", ct)).Value!;
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var producer = app.Services.GetRequiredService<SearchInteractionEventProducer>();
        var denied = await producer.BoardFilterChangedAsync(other, organization, board.Id, ct);
        Assert.False(denied.Succeeded); Assert.Null(denied.Value);
        await work.UpsertBoardMemberAsync(board.Id, other, BoardRole.Member, DateTimeOffset.UtcNow, ct);
        var accepted = await producer.BoardFilterChangedAsync(other, organization, board.Id, ct);
        Assert.True(accepted.Succeeded); Assert.NotNull(accepted.Value);
        Assert.Equal("BOARD_FILTER_CHANGED", accepted.Value.EventType); Assert.Equal("BoardFilter", accepted.Value.EntityType);
        Assert.Equal(other, accepted.Value.ActorId); Assert.Equal(organization, accepted.Value.OrganizationId); Assert.Equal(board.Id, accepted.Value.BoardId);
        Assert.Equal(accepted.Value.EventId, accepted.Value.EntityId); Assert.Equal(1, accepted.Value.Version); Assert.Empty(accepted.Value.Metadata);
        Assert.Equal(board.Version, (await work.FindBoardAsync(board.Id, ct))!.Version);
        var sources = app.Services.GetRequiredService<ISearchInteractionEventStore>();
        var observed = new ObservedSearchSource(sources);
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var proof = (SearchSourceActorFixture)app.Services.GetRequiredService<ICommandActorAuthorization>();
        var controlled = new SearchInteractionEventProducer(observed, unit, proof, app.Services.GetRequiredService<IClock>());
        proof.RefuseAt(4); // identity admission, Work pre/post proofs, producer final proof
        var refused = await controlled.BoardFilterChangedAsync(other, organization, board.Id, ct);
        Assert.Equal("session_unavailable", refused.ErrorCode); Assert.Null(refused.Value); Assert.NotNull(observed.Last);
        proof.RefuseAt(null);
        var replacement = SearchInteractionEvent.BoardFilterChanged(observed.Last.EventId, other, organization, board.Id, observed.Last.CreatedAt.AddSeconds(1));
        Assert.True((await unit.ExecuteAsync<bool>(other, async () =>
        { await sources.AppendAsync(replacement, ct); return IdentityOperation<bool>.Success(true); }, ct)).Succeeded);
        proof.RefuseAt(null);
        proof.OnProof = async check =>
        {
            if (check == 4) await work.RemoveBoardMemberAsync(board.Id, other, DateTimeOffset.UtcNow, ct);
        };
        var lostDuringProof = await controlled.BoardFilterChangedAsync(other, organization, board.Id, ct);
        Assert.False(lostDuringProof.Succeeded); Assert.Null(lostDuringProof.Value);
        proof.OnProof = null; proof.RefuseAt(null);
        Assert.NotNull(observed.Last);
        await work.UpsertBoardMemberAsync(board.Id, other, BoardRole.Member, DateTimeOffset.UtcNow, ct);
        var afterWithdrawal = SearchInteractionEvent.BoardFilterChanged(observed.Last.EventId, other, organization, board.Id, observed.Last.CreatedAt.AddSeconds(2));
        Assert.True((await unit.ExecuteAsync<bool>(other, async () =>
        { await sources.AppendAsync(afterWithdrawal, ct); return IdentityOperation<bool>.Success(true); }, ct)).Succeeded);
        await work.RemoveBoardMemberAsync(board.Id, other, DateTimeOffset.UtcNow, ct);
        var withdrawn = await producer.BoardFilterChangedAsync(other, organization, board.Id, ct);
        Assert.False(withdrawn.Succeeded); Assert.Null(withdrawn.Value);
    }
}
