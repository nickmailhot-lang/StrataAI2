using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class DemoMentionHandleTests
{
    private sealed class Actor : ICommandActorAuthorization
    {
        public bool Allowed = true;
        public Func<Guid,CancellationToken,Task<bool>>? Probe;
        public Task<bool> VerifyAsync(Guid user, CancellationToken ct = default) => Probe?.Invoke(user, ct) ?? Task.FromResult(Allowed);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private static ServiceProvider Demo(Actor? actor = null, IClock? clock = null)
    {
        var services = new ServiceCollection(); var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddSingleton<IClock>(clock ?? new SystemClock()); services.AddSingleton<ICommandActorAuthorization>(actor ?? new());
        services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        return services.BuildServiceProvider();
    }
    private static UserIdentity Account()
    {
        var email = $"{Guid.NewGuid():N}@example.test"; var at = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), email, email.ToUpperInvariant(), "Same name", null, "en", "UTC",
            AccountStatus.Active, true, "fixture", at, at, 1);
    }
    private static UserProfile Profile(UserIdentity user) => new(user.Id, user.Email, user.DisplayName, user.AvatarUrl,
        user.Locale, user.Timezone, user.Status, user.EmailVerified, user.CreatedAt, user.UpdatedAt, user.Version);
    private static async Task Seed(ServiceProvider provider, UserIdentity account, CancellationToken ct)
        => Assert.True(await provider.GetRequiredService<IIdentityStore>().TryCreateUserAsync(account, null, null, ct));
    private static Task<IdentityOperation<UserMentionHandleChange>> Claim(ServiceProvider provider, Guid user, string handle, long version, CancellationToken ct)
        => provider.GetRequiredService<IIdentityUnitOfWork>().ExecuteAsync(user,
            () => provider.GetRequiredService<IUserMentionHandleStore>().ClaimAsync(user, handle, version, DateTimeOffset.UtcNow.AddMinutes(1), ct), ct);
    private static async Task<UserMentionHandle?> Current(ServiceProvider provider, Guid user, CancellationToken ct)
    {
        var result = await provider.GetRequiredService<IIdentityUnitOfWork>().ExecuteAsync(user, async () =>
            IdentityOperation<UserMentionHandle?>.Success(await provider.GetRequiredService<IUserMentionHandleStore>().FindAsync(user, ct)), ct);
        Assert.True(result.Succeeded); return result.Value;
    }
    [Fact]
    public async Task PRD_02_15_ScopeCanonicalDefaultsClaimsHistoryAndHostIsolation()
    {
        var ct = TestContext.Current.CancellationToken; using var provider = Demo(); var user = Account(); var other = Account();
        await Seed(provider, user, ct); await Seed(provider, other, ct);
        var store = provider.GetRequiredService<IUserMentionHandleStore>(); var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(user.Id, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ClaimAsync(user.Id, "unowned", 1, DateTimeOffset.UtcNow, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(user.Id, async () =>
        { await store.FindAsync(other.Id, ct); return IdentityOperation<bool>.Success(true); }, ct));
        var initial = await Current(provider, user.Id, ct); Assert.NotNull(initial);
        Assert.Equal($"u_{user.Id:N}", initial.Handle); Assert.Equal(1, initial.Version); Assert.Equal(initial.CreatedAt, initial.UpdatedAt);
        var changed = await Claim(provider, user.Id, "  ALICE  ", 1, ct); Assert.True(changed.Succeeded);
        Assert.True(changed.Value!.Changed); Assert.Equal("alice", changed.Value.Current.Handle);
        Assert.Equal(2, changed.Value.Current.Version); Assert.Equal(initial.CreatedAt, changed.Value.Current.CreatedAt);
        var noop = await Claim(provider, user.Id, " Alice ", 2, ct); Assert.True(noop.Succeeded); Assert.False(noop.Value!.Changed);
        Assert.Equal(changed.Value.Current, noop.Value.Current);
        Assert.Equal("version_conflict", (await Claim(provider, user.Id, "alice", 1, ct)).ErrorCode);
        Assert.Equal("invalid_version", (await Claim(provider, user.Id, "valid", 0, ct)).ErrorCode);
        Assert.Equal("version_conflict", (await unit.ExecuteAsync(user.Id,
            () => store.ClaimAsync(user.Id, "valid", 2, initial.CreatedAt.AddSeconds(-1), ct), ct)).ErrorCode);
        foreach (var name in new[] { "card", "board", "u_other", $"u_{other.Id:N}", "nïck", "nick-name", "aa", new string('x', 41) })
            Assert.Equal("mention_handle_invalid", (await Claim(provider, user.Id, name, 2, ct)).ErrorCode);
        Assert.True((await Claim(provider, user.Id, initial.Handle, 2, ct)).Succeeded);
        Assert.Equal("mention_handle_unavailable", (await Claim(provider, other.Id, "alice", 1, ct)).ErrorCode);
        using var independent = Demo(); await Seed(independent, user, ct);
        Assert.Equal(1, (await Current(independent, user.Id, ct))!.Version);
        Assert.Equal(3, (await Current(provider, user.Id, ct))!.Version);
    }
    [Fact]
    public async Task PRD_15_ConcurrentClaimsRetainFormerAliasesAcrossDeactivationAndEnforceLifetimeBound()
    {
        var ct = TestContext.Current.CancellationToken; using var provider = Demo(); var a = Account(); var b = Account();
        await Seed(provider, a, ct); await Seed(provider, b, ct);
        var raced = await Task.WhenAll(Claim(provider, a.Id, "member", 1, ct), Claim(provider, b.Id, "member", 1, ct));
        Assert.Single(raced, x => x.Succeeded); Assert.Single(raced, x => x.ErrorCode == "mention_handle_unavailable");
        var winner = raced[0].Succeeded ? a : b; var loser = raced[0].Succeeded ? b : a;
        Assert.True((await Claim(provider, winner.Id, "renamed", 2, ct)).Succeeded);
        Assert.Equal("mention_handle_unavailable", (await Claim(provider, loser.Id, "member", 1, ct)).ErrorCode);
        for (var index = 0; index < MentionHandle.MaximumLifetimeReservations - 3; index++)
            Assert.True((await Claim(provider, winner.Id, "bounded_" + index, 3 + index, ct)).Succeeded);
        var current = await Current(provider, winner.Id, ct); Assert.Equal(32, current!.Version);
        Assert.Equal("mention_handle_claim_refused", (await Claim(provider, winner.Id, "overflow", 32, ct)).ErrorCode);
        Assert.Equal(current, await Current(provider, winner.Id, ct));
        Assert.True((await Claim(provider, winner.Id, "member", 32, ct)).Succeeded);
        Assert.True(await provider.GetRequiredService<IIdentityStore>().DeactivateUserAsync(winner.Id, DateTimeOffset.UtcNow, ct));
        Assert.Equal("member", (await Current(provider, winner.Id, ct))!.Handle);
        Assert.Equal("mention_handle_unavailable", (await Claim(provider, loser.Id, "member", 1, ct)).ErrorCode);
    }
    [Theory]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task PRD_02_15_FailedIdentityCommandRestoresHandleReservationProfileEventsAndReceipt(string mode)
    {
        var ct = TestContext.Current.CancellationToken; using var provider = Demo(); var user = Account(); var claimant = Account();
        await Seed(provider, user, ct); await Seed(provider, claimant, ct);
        var identity = provider.GetRequiredService<IIdentityStore>(); var store = provider.GetRequiredService<IUserMentionHandleStore>();
        var receipts = provider.GetRequiredService<IIdentityProfileReplayStore>(); var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var handleReceipts = provider.GetRequiredService<IIdentityHandleClaimReplayStore>();
        var initial = await Current(provider, user.Id, ct); var key = Guid.NewGuid(); using var canceled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task<IdentityOperation<bool>> Execute()
        {
            Assert.True((await store.ClaimAsync(user.Id, "must_rollback", 1, DateTimeOffset.UtcNow.AddMinutes(1), ct)).Succeeded);
            var updated = await identity.UpdateProfileAsync(user.Id, "Tentative", null, "en", "UTC", 1, DateTimeOffset.UtcNow, ct);
            Assert.NotNull(updated); await identity.AppendDomainEventAsync(user.Id, "USER_PROFILE_UPDATED", "fixture", ct);
            await receipts.SaveAsync(user.Id, key, new("fixture", Profile(updated)), ct);
            Assert.True(await handleReceipts.TrySaveAsync(user.Id, key, new string('a', 64), new(updated.Version, 2, true), ct));
            if (mode == "exception") throw new InvalidOperationException("fixture_failure");
            if (mode == "cancel") { canceled.Cancel(); return IdentityOperation<bool>.Success(true); }
            return IdentityOperation<bool>.Failure("fixture_refused");
        }
        if (mode == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(user.Id, Execute, ct));
        else if (mode == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(user.Id, Execute, canceled.Token));
        else Assert.Equal("fixture_refused", (await unit.ExecuteAsync(user.Id, Execute, ct)).ErrorCode);
        Assert.Equal(initial, await Current(provider, user.Id, ct)); Assert.Equal(user, await identity.FindUserByIdAsync(user.Id, ct));
        var inspected = await unit.ExecuteAsync(user.Id, async () =>
        {
            Assert.Null(await receipts.ReadAsync(user.Id, key, ct));
            Assert.Null(await handleReceipts.ReadAsync(user.Id, key, ct));
            var events = await identity.ReadEventsAsync(user.Id, 0, ct); Assert.True(events.Succeeded); Assert.Empty(events.Value!.Events);
            return IdentityOperation<bool>.Success(true);
        }, ct); Assert.True(inspected.Succeeded);
        Assert.True((await Claim(provider, claimant.Id, "must_rollback", 1, ct)).Succeeded);
    }
    [Fact]
    public async Task PRD_02_FailedRegistrationDoesNotReserveIdentityOrDefaultAndRetrySeedsExactlyOnce()
    {
        var ct = TestContext.Current.CancellationToken; using var provider = Demo(); var user = Account();
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>(); var identities = provider.GetRequiredService<IIdentityStore>();
        var failed = await unit.ExecuteRegistrationAsync(async () =>
        {
            Assert.True(await identities.TryCreateUserAsync(user, null, null, ct));
            await identities.AppendDomainEventAsync(user.Id, "USER_REGISTERED", "fixture", ct);
            return IdentityOperation<RegistrationOutcome>.Failure("fixture_refused");
        }, ct);
        Assert.Equal("fixture_refused", failed.ErrorCode); Assert.Null(await identities.FindUserByIdAsync(user.Id, ct));
        Assert.Null(await Current(provider, user.Id, ct));
        var retry = await unit.ExecuteRegistrationAsync(async () =>
        {
            Assert.True(await identities.TryCreateUserAsync(user, null, null, ct));
            return IdentityOperation<RegistrationOutcome>.Success(new(Profile(user), null));
        }, ct);
        Assert.True(retry.Succeeded); Assert.Equal($"u_{user.Id:N}", (await Current(provider, user.Id, ct))!.Handle);
        Assert.True((await Claim(provider, user.Id, "recovered", 1, ct)).Succeeded);
    }
    [Fact]
    public async Task PRD_24_DeniedActorNeverEntersProtectedHandleOperationAndNestedAccountCommandIsRefused()
    {
        var ct = TestContext.Current.CancellationToken; var actor = new Actor(); using var provider = Demo(actor); var user = Account();
        await Seed(provider, user, ct); var unit = provider.GetRequiredService<IIdentityUnitOfWork>(); actor.Allowed = false;
        var called = false; var refused = await unit.ExecuteAsync(user.Id, () =>
        { called = true; return Task.FromResult(IdentityOperation<bool>.Success(true)); }, ct);
        Assert.Equal("session_unavailable", refused.ErrorCode); Assert.False(called); actor.Allowed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(user.Id,
            () => unit.ExecuteAsync(user.Id, () => Task.FromResult(IdentityOperation<bool>.Success(true)), ct), ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(user.Id, async () =>
        {
            await unit.ExecuteTokenProofAsync(() => Task.FromResult(IdentityOperation<UserProfile>.Success(Profile(user))), ct);
            return IdentityOperation<bool>.Success(true);
        }, ct));
        Assert.Equal(1, (await Current(provider, user.Id, ct))!.Version);
    }
    [Fact]
    public async Task PRD_02_15_HandleReceiptsRetainImmutableMetadataExactExpiryAndCurrentSubjectScope()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new Clock(); using var provider = Demo(clock: clock);
        var user = Account(); var other = Account(); await Seed(provider, user, ct); await Seed(provider, other, ct);
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>(); var store = provider.GetRequiredService<IIdentityHandleClaimReplayStore>();
        var key = Guid.NewGuid(); var fingerprint = new string('a', 64); var receipt = new HandleClaimReceipt(1, 1, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(user.Id, key, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(user.Id, async () =>
        { await store.TrySaveAsync(other.Id, key, fingerprint, receipt, ct); return IdentityOperation<bool>.Success(true); }, ct));
        var saved = await unit.ExecuteAsync(user.Id, async () =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.TrySaveAsync(user.Id, key, "raw_handle", receipt, ct));
            await Assert.ThrowsAsync<ArgumentException>(() => store.TrySaveAsync(user.Id, Guid.Empty, fingerprint, receipt, ct));
            await Assert.ThrowsAsync<ArgumentException>(() => store.TrySaveAsync(user.Id, key, fingerprint, new(0, 1, false), ct));
            Assert.True(await store.TrySaveAsync(user.Id, key, fingerprint, receipt, ct));
            Assert.False(await store.TrySaveAsync(user.Id, key, new string('b', 64), new(2, 2, true), ct));
            return IdentityOperation<IdentityHandleClaimReplay?>.Success(await store.ReadAsync(user.Id, key, ct));
        }, ct);
        Assert.True(saved.Succeeded); var original = saved.Value; Assert.NotNull(original);
        Assert.Equal(fingerprint, original.Fingerprint); Assert.Equal(receipt, original.Receipt); Assert.False(original.Expired);
        Assert.Equal(TimeSpan.FromHours(24), original.ExpiresAt - original.CreatedAt);
        clock.UtcNow = original.ExpiresAt;
        var expired = await unit.ExecuteAsync(user.Id, async () =>
        {
            Assert.False(await store.TrySaveAsync(user.Id, key, new string('b', 64), new(2, 2, true), ct));
            return IdentityOperation<IdentityHandleClaimReplay?>.Success(await store.ReadAsync(user.Id, key, ct));
        }, ct);
        Assert.Equal(original with { Expired = true }, expired.Value);
        var isolated = await unit.ExecuteAsync(other.Id, async () =>
        {
            Assert.Null(await store.ReadAsync(other.Id, key, ct));
            return IdentityOperation<bool>.Success(await store.TrySaveAsync(other.Id, key, fingerprint, receipt, ct));
        }, ct); Assert.True(isolated.Succeeded); Assert.True(isolated.Value);
    }
    [Fact]
    public async Task PRD_02_15_AccountHandleCommandAcknowledgesNormalizedOriginalIntentAndAtomicUserEvent()
    {
        var ct = TestContext.Current.CancellationToken; using var provider = Demo(); var user = Account(); await Seed(provider, user, ct);
        var service = provider.GetRequiredService<UserMentionHandleService>(); var key = Guid.NewGuid();
        var input = new ClaimMentionHandleInput("  ALICE  ", 1, 1);
        var initial = await service.GetAsync(user.Id, ct); Assert.True(initial.Succeeded); Assert.Equal(1, initial.Value!.UserVersion);
        var changed = await service.ClaimAsync(user.Id, key, input, "fixture", ct); Assert.True(changed.Succeeded);
        Assert.Equal(new HandleClaimAcknowledgment(user.Id, "alice", 2, 2, true), changed.Value);
        Assert.Equal(changed.Value, (await service.ClaimAsync(user.Id, key, input with { Handle = "alice" }, "retry", ct)).Value);
        Assert.Equal("idempotency_key_reused", (await service.ClaimAsync(user.Id, key, input with { Handle = "other" }, "fixture", ct)).ErrorCode);
        Assert.Equal("version_conflict", (await service.ClaimAsync(user.Id, Guid.NewGuid(), input, "fixture", ct)).ErrorCode);
        var noop = await service.ClaimAsync(user.Id, Guid.NewGuid(), new(" Alice ", 2, 2), "fixture", ct);
        Assert.Equal(new HandleClaimAcknowledgment(user.Id, "alice", 2, 2, false), noop.Value);
        var identity = provider.GetRequiredService<IIdentityStore>(); var current = await identity.FindUserByIdAsync(user.Id, ct);
        Assert.Equal(user with { Version = 2, UpdatedAt = current!.UpdatedAt }, current);
        var events = await identity.ReadEventsAsync(user.Id, 0, ct); Assert.True(events.Succeeded);
        var only = Assert.Single(events.Value!.Events); Assert.Equal("USER_PROFILE_UPDATED", only.EventType);
        Assert.Equal(2, only.Version); Assert.Empty(only.Metadata);
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var receipt = await unit.ExecuteAsync(user.Id, async () => IdentityOperation<IdentityHandleClaimReplay?>.Success(
            await provider.GetRequiredService<IIdentityHandleClaimReplayStore>().ReadAsync(user.Id, key, ct)), ct);
        Assert.Equal(new HandleClaimReceipt(2, 2, true), receipt.Value!.Receipt);
    }
    [Fact]
    public async Task PRD_02_15_OriginalHandleReceiptSurvivesOtherProfileChangesButNeverHydratesFormerAlias()
    {
        var ct = TestContext.Current.CancellationToken; using var provider = Demo(); var user = Account(); await Seed(provider, user, ct);
        var service = provider.GetRequiredService<UserMentionHandleService>(); var first = new ClaimMentionHandleInput("alice", 1, 1); var key = Guid.NewGuid();
        var result = await service.ClaimAsync(user.Id, key, first, "fixture", ct); Assert.True(result.Succeeded);
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>(); var identity = provider.GetRequiredService<IIdentityStore>();
        var peer = await unit.ExecuteAsync(user.Id, async () => IdentityOperation<UserIdentity?>.Success(
            await identity.UpdateProfileAsync(user.Id, "New display name", null, "en", "UTC", 2, DateTimeOffset.UtcNow, ct)), ct);
        Assert.True(peer.Succeeded); Assert.Equal(3, peer.Value!.Version);
        Assert.Equal(result.Value, (await service.ClaimAsync(user.Id, key, first, "retry", ct)).Value);
        Assert.True((await service.ClaimAsync(user.Id, Guid.NewGuid(), new("renamed", 3, 2), "fixture", ct)).Succeeded);
        Assert.Equal("mention_handle_unavailable", (await service.ClaimAsync(user.Id, key, first, "retry", ct)).ErrorCode);
        Assert.True((await service.ClaimAsync(user.Id, Guid.NewGuid(), new("alice", 4, 3), "fixture", ct)).Succeeded);
        Assert.Equal("mention_handle_unavailable", (await service.ClaimAsync(user.Id, key, first, "retry", ct)).ErrorCode);
    }
    [Theory]
    [InlineData("session")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task PRD_02_24_LateHandleCommandAdmissionRestoresUserAliasEventAndOriginalReceipt(string mode)
    {
        var ct = TestContext.Current.CancellationToken; var actor = new Actor(); using var provider = Demo(actor); var user = Account(); await Seed(provider, user, ct);
        var key = Guid.NewGuid(); var service = provider.GetRequiredService<UserMentionHandleService>();
        var receipts = provider.GetRequiredService<IIdentityHandleClaimReplayStore>(); using var canceled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        actor.Probe = async (id, _) =>
        {
            if (await receipts.ReadAsync(id, key, ct) is null) return true;
            if (mode == "exception") throw new InvalidOperationException("late_fixture_failure");
            if (mode == "cancel") { canceled.Cancel(); return true; }
            return false;
        };
        var input = new ClaimMentionHandleInput("must_rollback", 1, 1);
        if (mode == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClaimAsync(user.Id, key, input, "fixture", ct));
        else if (mode == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ClaimAsync(user.Id, key, input, "fixture", canceled.Token));
        else Assert.Equal("session_unavailable", (await service.ClaimAsync(user.Id, key, input, "fixture", ct)).ErrorCode);
        actor.Probe = null; var snapshot = await service.GetAsync(user.Id, ct); Assert.True(snapshot.Succeeded);
        Assert.Equal(1, snapshot.Value!.UserVersion); Assert.Equal(1, snapshot.Value.HandleVersion); Assert.Equal($"u_{user.Id:N}", snapshot.Value.Handle);
        var identity = provider.GetRequiredService<IIdentityStore>(); Assert.Equal(user, await identity.FindUserByIdAsync(user.Id, ct));
        Assert.Empty((await identity.ReadEventsAsync(user.Id, 0, ct)).Value!.Events);
        Assert.True((await service.ClaimAsync(user.Id, key, input, "retry", ct)).Succeeded);
        Assert.Single((await identity.ReadEventsAsync(user.Id, 0, ct)).Value!.Events);
    }
    [Fact]
    public async Task PRD_02_24_InvalidConflictingExpiredAndDeniedHandleCommandsPreserveCurrentAccount()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new Clock(); var actor = new Actor(); using var provider = Demo(actor, clock);
        var user = Account(); var other = Account(); await Seed(provider, user, ct); await Seed(provider, other, ct);
        clock.UtcNow = DateTimeOffset.UtcNow.AddMinutes(1); var service = provider.GetRequiredService<UserMentionHandleService>();
        foreach (var handle in new[] { "card", "board", "u_other", $"u_{other.Id:N}", "nïck", "aa" })
            Assert.Equal("mention_handle_invalid", (await service.ClaimAsync(user.Id, Guid.NewGuid(), new(handle, 1, 1), "fixture", ct)).ErrorCode);
        Assert.Equal("invalid_idempotency_key", (await service.ClaimAsync(user.Id, Guid.Empty, new("valid", 1, 1), "fixture", ct)).ErrorCode);
        Assert.Equal("invalid_version", (await service.ClaimAsync(user.Id, Guid.NewGuid(), new("valid", 0, 1), "fixture", ct)).ErrorCode);
        Assert.Equal("version_conflict", (await service.ClaimAsync(user.Id, Guid.NewGuid(), new("valid", 1, 2), "fixture", ct)).ErrorCode);
        var key = Guid.NewGuid(); var input = new ClaimMentionHandleInput("occupied", 1, 1);
        Assert.True((await service.ClaimAsync(other.Id, key, input, "fixture", ct)).Succeeded);
        Assert.Equal("mention_handle_unavailable", (await service.ClaimAsync(user.Id, Guid.NewGuid(), input, "fixture", ct)).ErrorCode);
        Assert.Equal(1, (await service.GetAsync(user.Id, ct)).Value!.UserVersion);
        clock.UtcNow = clock.UtcNow.AddHours(24);
        Assert.Equal("idempotency_key_expired", (await service.ClaimAsync(other.Id, key, input, "retry", ct)).ErrorCode);
        actor.Allowed = false; Assert.Equal("session_unavailable", (await service.GetAsync(other.Id, ct)).ErrorCode);
        Assert.Equal("session_unavailable", (await service.ClaimAsync(other.Id, key, input, "retry", ct)).ErrorCode);
    }
}
