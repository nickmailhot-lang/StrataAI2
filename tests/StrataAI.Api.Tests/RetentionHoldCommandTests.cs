using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // Command contract probe only; runtime persistence/audit is not established.
    private sealed class HoldProbe : IRetentionHoldStore
    {
        public Task<RetentionHoldCandidates> ReadApplicableAsync(RetentionRecord record, CancellationToken ct) =>
            throw new NotSupportedException("Command probe does not establish complete owning hold lookup.");
        public Task<RetentionHoldReference?> ReadReferenceAsync(Guid organization, Guid hold, CancellationToken ct) => Task.FromResult(Reference(Current));
        public Task<RetentionHoldReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => Task.FromResult(Reference(Receipt?.Key == key ? Receipt.Result : null));
        private static RetentionHoldReference? Reference(RetentionHold? hold) => hold is null ? null : new(hold.OrganizationId, hold.Id,
            hold.Version, hold.RecordClass, hold.Category is not null, hold.RecordId, null, null, null, null);
        public Task<IReadOnlyList<RetentionHoldRevision>> ReadHistoryAsync(Guid organization, Guid hold, CancellationToken ct) => throw new NotSupportedException("Command probe does not establish history.");
        public RetentionHold? Current { get; set; }
        public RetentionHoldReceipt? Receipt { get; set; }
        public RetentionHoldWrite? LastWrite { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Action? AfterRead { get; set; }
        public Action? AfterWrite { get; set; }
        public Task<RetentionHold?> ReadAsync(Guid organization, Guid hold, CancellationToken ct)
        { Reads++; AfterRead?.Invoke(); return Task.FromResult(Current); }
        public Task<RetentionHoldReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
        { Reads++; return Task.FromResult(Receipt?.Key == key ? Receipt : null); }
        public Task<RetentionHoldWriteResult> WriteAsync(long expectedVersion, RetentionHoldWrite write, CancellationToken ct)
        { Writes++; LastWrite = write; Current = write.Hold; Receipt = write.Receipt; AfterWrite?.Invoke(); return Task.FromResult(RetentionHoldWriteResult.Written); }
    }
    private sealed class HoldScopeProbe : IRetentionHoldScopeAuthorization
    {
        public bool Allowed { get; set; } = true;
        public bool ReferenceAllowed { get; set; } = true;
        public Task<bool> VerifyAsync(RetentionHoldReference reference, Guid actor, CancellationToken ct) => Task.FromResult(Allowed && ReferenceAllowed);
        public int Reads { get; private set; }
        public RetentionHoldScope? LastScope { get; private set; }
        public Task<bool> VerifyAsync(RetentionHoldScope scope, Guid actor, CancellationToken ct)
        { Reads++; LastScope = scope; return Task.FromResult(Allowed); }
    }

    [Fact]
    public async Task PRD_70_hold_reference_denial_precedes_private_bodies_retry_and_stale_release()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Private reference gates", ct);
        var store = new HoldProbe(); var scopes = new HoldScopeProbe(); var clock = new ConfigurationCommandClock();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, store, scopes, clock);
        var scope = new RetentionHoldScope(parent.Id, RetentionRecordClass.Governance, "decisions", Guid.NewGuid());
        var id = Guid.NewGuid(); var key = Guid.NewGuid();
        Assert.True((await service.ApplyAsync(parent.Id, actor, id, scope, "Private evidence", key, "apply", ct)).Succeeded);
        var reads = store.Reads; var writes = store.Writes;
        scopes.ReferenceAllowed = false;
        Assert.Equal("retention_hold_not_found", (await service.ReadAsync(parent.Id, actor, id, ct)).ErrorCode);
        Assert.Equal("retention_hold_not_found", (await service.ReleaseAsync(parent.Id, actor, id, 999, Guid.NewGuid(), "stale", ct)).ErrorCode);
        Assert.Equal("retention_hold_not_found", (await service.ApplyAsync(parent.Id, actor, id, scope, "Private evidence", key, "retry", ct)).ErrorCode);
        Assert.Equal(reads, store.Reads); Assert.Equal(writes, store.Writes);
    }

    [Fact]
    public async Task PRD_70_hold_changed_reference_refuses_private_read_acknowledgment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Reference changes", ct);
        var store = new HoldProbe(); var scopes = new HoldScopeProbe(); var clock = new ConfigurationCommandClock();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, store, scopes, clock);
        var id = Guid.NewGuid();
        Assert.True((await service.ApplyAsync(parent.Id, actor, id,
            new(parent.Id, RetentionRecordClass.Governance, null, Guid.NewGuid()), "Private evidence", Guid.NewGuid(), "apply", ct)).Succeeded);
        var writes = store.Writes;
        store.AfterRead = () => store.Current = store.Current! with { Version = store.Current.Version + 1 };
        Assert.Equal("retention_hold_source_unavailable", (await service.ReadAsync(parent.Id, actor, id, ct)).ErrorCode);
        Assert.Equal(writes, store.Writes);
    }

    [Fact]
    public async Task PRD_70_hold_command_retains_exact_apply_and_release_receipts_original_sources_and_audit_intent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold transitions", ct);
        var store = new HoldProbe(); var scopes = new HoldScopeProbe(); var clock = new ConfigurationCommandClock();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, store, scopes, clock);
        var scope = new RetentionHoldScope(parent.Id, RetentionRecordClass.Governance, "decisions", Guid.NewGuid());
        var id = Guid.NewGuid(); var applyKey = Guid.NewGuid(); var releaseKey = Guid.NewGuid();
        var applied = await service.ApplyAsync(parent.Id, actor, id, scope, "Explicit hold evidence", applyKey, "apply", ct);
        Assert.True(applied.Succeeded); Assert.Equal(1, applied.Value!.Version); Assert.Equal(0, applied.Value.AppliedAt.Ticks % 10);
        var originalReceipt = store.Receipt; var originalWrite = store.LastWrite!;
        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        var released = await service.ReleaseAsync(parent.Id, actor, id, 1, releaseKey, "release", ct);
        Assert.True(released.Succeeded); Assert.Equal(2, released.Value!.Version);
        Assert.Equal(applied.Value with { Version = 2, Status = RetentionHoldStatus.Released, ReleasedBy = actor, ReleasedAt = released.Value.ReleasedAt }, released.Value);
        var releaseReceipt = store.Receipt;
        Assert.Equal(actor, store.LastWrite!.ActorId); Assert.Equal(OrganizationRole.Owner, store.LastWrite.ActingCapacity);
        Assert.NotEqual(Guid.Empty, store.LastWrite.MembershipId); Assert.True(store.LastWrite.MembershipVersion > 0);
        Assert.NotEqual(Guid.Empty, store.LastWrite.EventId); Assert.NotEqual(originalWrite.EventId, store.LastWrite.EventId);
        Assert.Equal("release", store.LastWrite.CorrelationId); Assert.Equal(released.Value, store.LastWrite.Receipt.Result);
        Assert.Equal(released.Value.ReleasedAt!.Value.AddHours(24), store.LastWrite.Receipt.ExpiresAt);
        Assert.Equal(released.Value, (await service.ReleaseAsync(parent.Id, actor, id, 1, releaseKey, "retry", ct)).Value);
        store.Receipt = releaseReceipt! with { Result = releaseReceipt.Result with { Reason = "Substituted application evidence" } };
        Assert.Equal("retention_hold_source_unavailable", (await service.ReleaseAsync(parent.Id, actor, id, 1, releaseKey, "corrupt retry", ct)).ErrorCode);
        store.Receipt = originalReceipt;
        Assert.Equal(applied.Value, (await service.ApplyAsync(parent.Id, actor, id, scope, "Explicit hold evidence", applyKey, "original retry", ct)).Value);
        Assert.Equal("idempotency_key_conflict", (await service.ApplyAsync(parent.Id, actor, id, scope, "Changed evidence", applyKey, "changed", ct)).ErrorCode);
        store.Receipt = releaseReceipt;
        Assert.Equal("version_conflict", (await service.ReleaseAsync(parent.Id, actor, id, 1, Guid.NewGuid(), "stale", ct)).ErrorCode);
        Assert.Equal("retention_hold_already_released", (await service.ReleaseAsync(parent.Id, actor, id, 2, Guid.NewGuid(), "repeat", ct)).ErrorCode);
        clock.UtcNow = releaseReceipt!.ExpiresAt;
        Assert.Equal("idempotency_key_expired", (await service.ReleaseAsync(parent.Id, actor, id, 1, releaseKey, "expired", ct)).ErrorCode);
        Assert.Equal(2, store.Writes); Assert.Equal(released.Value, store.Current);
    }

    [Fact]
    public async Task PRD_70_hold_command_admits_parent_before_validation_and_owning_scope_before_receipt_IO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(foreign);
        var (actor, parent) = await ConfigurationParent(owner, "Hold admission", ct);
        var (outsider, _) = await ConfigurationParent(foreign, "Outside hold", ct);
        var store = new HoldProbe(); var scopes = new HoldScopeProbe();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, store, scopes);
        var scope = new RetentionHoldScope(parent.Id, null, null, null);
        Assert.Equal("organization_not_found", (await service.ApplyAsync(parent.Id, outsider, Guid.Empty, scope, "", Guid.Empty, "", ct)).ErrorCode);
        Assert.Equal("organization_not_found", (await service.ReleaseAsync(parent.Id, outsider, Guid.Empty, 0, Guid.Empty, "", ct)).ErrorCode);
        Assert.Equal(0, store.Reads); Assert.Equal(0, store.Writes); Assert.Equal(0, scopes.Reads);
        Assert.Equal("invalid_retention_hold", (await service.ApplyAsync(parent.Id, actor, Guid.NewGuid(), scope with { Category = "contracts" }, "Reason", Guid.NewGuid(), "invalid", ct)).ErrorCode);
        Assert.Equal(0, scopes.Reads); Assert.Equal(0, store.Reads);
        scopes.Allowed = false;
        Assert.Equal("retention_hold_not_found", (await service.ApplyAsync(parent.Id, actor, Guid.NewGuid(), scope, "Reason", Guid.NewGuid(), "denied", ct)).ErrorCode);
        Assert.Equal(0, store.Reads); Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task PRD_70_hold_scope_denial_withdraws_read_release_retry_and_conflict_information()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold scope fence", ct);
        var store = new HoldProbe(); var scopes = new HoldScopeProbe();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, store, scopes);
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var scope = new RetentionHoldScope(parent.Id, RetentionRecordClass.Document, null, Guid.NewGuid());
        Assert.True((await service.ApplyAsync(parent.Id, actor, id, scope, "Reason", key, "initial", ct)).Succeeded);
        scopes.Allowed = false;
        Assert.Equal("retention_hold_not_found", (await service.ReadAsync(parent.Id, actor, id, ct)).ErrorCode);
        Assert.Equal("retention_hold_not_found", (await service.ReleaseAsync(parent.Id, actor, id, 99, Guid.NewGuid(), "stale private", ct)).ErrorCode);
        Assert.Equal("retention_hold_not_found", (await service.ApplyAsync(parent.Id, actor, id, scope, "Reason", key, "retry", ct)).ErrorCode);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task PRD_70_hold_command_refuses_foreign_corrupt_future_and_substituted_receipt_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold sources", ct);
        var store = new HoldProbe(); var scopes = new HoldScopeProbe(); var clock = new ConfigurationCommandClock();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, store, scopes, clock);
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var scope = new RetentionHoldScope(parent.Id, null, null, null);
        var applied = await service.ApplyAsync(parent.Id, actor, id, scope, "Reason", key, "initial", ct);
        Assert.True(applied.Succeeded); var receipt = store.Receipt!;
        foreach (var corrupted in new[] { applied.Value! with { OrganizationId = Guid.NewGuid() }, applied.Value! with { Id = Guid.NewGuid() },
            applied.Value! with { AppliedAt = clock.UtcNow.AddDays(1) }, applied.Value! with { ReleasedBy = actor } })
        {
            store.Current = corrupted;
            Assert.Equal("retention_hold_source_unavailable", (await service.ReadAsync(parent.Id, actor, id, ct)).ErrorCode);
        }
        store.Current = applied.Value;
        foreach (var corrupted in new[] { receipt with { ActorId = Guid.NewGuid() }, receipt with { ExpiresAt = receipt.ExpiresAt.AddTicks(1) },
            receipt with { Result = receipt.Result with { Reason = "Substituted source" } }, receipt with { Fingerprint = "malformed" } })
        {
            store.Receipt = corrupted;
            Assert.Equal("retention_hold_source_unavailable", (await service.ApplyAsync(parent.Id, actor, id, scope, "Reason", key, "retry", ct)).ErrorCode);
        }
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task PRD_70_hold_command_final_scope_and_account_fences_refuse_success_after_write_or_read()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold final fences", ct);
        var store = new HoldProbe(); var scopes = new HoldScopeProbe();
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<ICommandActorAuthorization>());
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, store, scopes, fence);
        store.AfterWrite = () => scopes.Allowed = false;
        var denied = await service.ApplyAsync(parent.Id, actor, Guid.NewGuid(), new(parent.Id, null, null, null), "Reason", Guid.NewGuid(), "withdraw", ct);
        Assert.False(denied.Succeeded); Assert.Null(denied.Value); Assert.Equal("retention_hold_not_found", denied.ErrorCode);
        // A probe cannot prove adapter rollback; runtime adapters must participate
        // in this owning transaction and will be tested separately before activation.
        scopes.Allowed = true; store.AfterRead = () => fence.Denied = true;
        var read = await service.ReadAsync(parent.Id, actor, store.Current!.Id, ct);
        Assert.False(read.Succeeded); Assert.Null(read.Value); Assert.Equal("organization_not_found", read.ErrorCode);
    }
}
