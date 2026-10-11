using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationOperationalLifecycleTests
{
    private static readonly Guid Organization = Guid.NewGuid(), Actor = Guid.NewGuid(), Key = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OrganizationStatus.Active, OrganizationStatus.Suspended, "ORGANIZATION_SUSPENDED")]
    [InlineData(OrganizationStatus.Active, OrganizationStatus.Deactivated, "ORGANIZATION_DEACTIVATED")]
    [InlineData(OrganizationStatus.Suspended, OrganizationStatus.Deactivated, "ORGANIZATION_DEACTIVATED")]
    [InlineData(OrganizationStatus.Suspended, OrganizationStatus.Active, "ORGANIZATION_REACTIVATED")]
    [InlineData(OrganizationStatus.Deactivated, OrganizationStatus.Suspended, "ORGANIZATION_SUSPENDED")]
    [InlineData(OrganizationStatus.Deactivated, OrganizationStatus.Active, "ORGANIZATION_REACTIVATED")]
    public async Task PRD_27_FR007_Reviewed_transitions_preserve_original_actor_capacity_and_source(OrganizationStatus before,
        OrganizationStatus after, string eventType)
    {
        var f = new Fixture(); f.Store.Scope = f.Store.Scope! with { Status = before };
        var membership = f.Store.Scope.MembershipId;
        var result = await f.Change(after);
        Assert.True(result.Succeeded); var source = result.Value!;
        Assert.Equal(before, source.PreviousStatus); Assert.Equal(after, source.Status); Assert.Equal(8, source.Version);
        Assert.Equal(eventType, source.EventType); Assert.Equal(Organization, source.EntityId);
        Assert.Equal("Organization", source.EntityType); Assert.Equal(Actor, source.ActorId);
        Assert.Equal("Original administrator", source.ActorLabel); Assert.Equal("ORGANIZATION_OWNER", source.ActingCapacity);
        Assert.Equal(membership, source.MembershipId); Assert.Equal(3, source.MembershipVersion);
        Assert.Equal(Now, source.CreatedAt); Assert.Equal("reviewed-change", source.CorrelationId);
        Assert.Same(source, Assert.Single(f.Store.Sources)); Assert.Same(source, f.Store.Receipt!.Result);
        Assert.Equal(1, f.Unit.Commits); Assert.Equal(0, f.Unit.Rollbacks);
    }

    [Fact]
    public async Task PRD_27_FR007_Admin_capacity_is_captured_and_original_retry_never_restores_later_state()
    {
        var f = new Fixture(); f.Store.Scope = f.Store.Scope! with { Role = OrganizationRole.Admin };
        var original = await f.Change(OrganizationStatus.Suspended); Assert.True(original.Succeeded);
        Assert.Equal("ORGANIZATION_ADMIN", original.Value!.ActingCapacity);
        // Another admitted lifecycle command has since reactivated the parent.
        f.Store.Scope = f.Store.Scope! with { Status = OrganizationStatus.Active, Version = 9,
            UpdatedAt = Now.AddMinutes(1), ActorLabel = "Later caption" };
        var recovered = await f.Change(OrganizationStatus.Suspended);
        Assert.True(recovered.Succeeded); Assert.Same(original.Value, recovered.Value);
        Assert.Equal(OrganizationStatus.Active, f.Store.Scope.Status); Assert.Equal(9, f.Store.Scope.Version);
        Assert.Single(f.Store.Sources); Assert.Equal(1, f.Store.Writes);
    }

    [Fact]
    public async Task PRD_27_FR007_Withdrawn_foreign_terminal_or_non_admin_scope_reads_no_receipt_or_source()
    {
        var baseline = new Fixture().Store.Scope!;
        foreach (var scope in new OrganizationOperationalLifecycleScope?[] { null,
            baseline with { OrganizationId = Guid.NewGuid() }, baseline with { ActorId = Guid.NewGuid() },
            baseline with { MembershipActive = false }, baseline with { MembershipId = Guid.Empty },
            baseline with { MembershipVersion = 0 }, baseline with { Role = OrganizationRole.Member },
            baseline with { Role = (OrganizationRole)999 }, baseline with { Status = OrganizationStatus.Archived },
            baseline with { Status = OrganizationStatus.Deleting }, baseline with { Status = OrganizationStatus.Deleted },
            baseline with { Status = (OrganizationStatus)999 }, baseline with { ActorLabel = "\0caption" } })
        {
            var f = new Fixture(); f.Store.Scope = scope;
            var result = await f.Change((OrganizationStatus)999, version: 0, key: Guid.Empty);
            Assert.Equal("organization_not_found", result.ErrorCode); Assert.Null(result.Value);
            Assert.Equal(0, f.Store.ReceiptReads); Assert.Equal(0, f.Store.Writes);
        }
    }

    [Theory]
    [InlineData(OrganizationStatus.Archived)] [InlineData(OrganizationStatus.Deleting)]
    [InlineData(OrganizationStatus.Deleted)] [InlineData((OrganizationStatus)999)]
    public async Task PRD_27_FR007_Operational_command_cannot_enter_archive_or_deletion(OrganizationStatus desired)
    {
        var f = new Fixture(); Assert.Equal("invalid_organization_lifecycle", (await f.Change(desired)).ErrorCode);
        Assert.Equal(0, f.Store.Writes); Assert.Equal(0, f.Store.ReceiptReads);
    }

    [Fact]
    public async Task PRD_27_FR007_Invalid_review_key_version_correlation_or_clock_has_no_effect()
    {
        foreach (var version in new[] { 0L, -1L, 6L, long.MaxValue })
        {
            var f = new Fixture(); Assert.Equal("version_conflict", (await f.Change(version: version)).ErrorCode);
            Assert.Empty(f.Store.Sources); Assert.Null(f.Store.Receipt);
        }
        foreach (var correlation in new[] { "", "\0private", new string('x', 257) })
        {
            var f = new Fixture(); Assert.Equal("invalid_correlation_id", (await f.Change(correlation: correlation)).ErrorCode);
            Assert.Equal(0, f.Store.Writes);
        }
        var keyFixture = new Fixture(); Assert.Equal("idempotency_key_required", (await keyFixture.Change(key: Guid.Empty)).ErrorCode);
        var clockFixture = new Fixture(); clockFixture.Clock.Value = Now.AddDays(-1);
        Assert.Equal("version_conflict", (await clockFixture.Change()).ErrorCode); Assert.Equal(0, clockFixture.Store.Writes);
        var unchanged = new Fixture(); Assert.Equal("organization_lifecycle_unchanged", (await unchanged.Change(OrganizationStatus.Active)).ErrorCode);
        Assert.Equal(0, unchanged.Store.Writes);
    }

    [Fact]
    public async Task PRD_27_FR007_Original_receipt_rejects_changed_intent_expiry_and_corrupted_scope()
    {
        var f = new Fixture(); Assert.True((await f.Change()).Succeeded);
        Assert.Equal("idempotency_key_conflict", (await f.Change(OrganizationStatus.Deactivated)).ErrorCode);
        Assert.Equal("idempotency_key_conflict", (await f.Change(version: 8)).ErrorCode);
        var receipt = f.Store.Receipt!; f.Clock.Value = receipt.ExpiresAt;
        Assert.Equal("idempotency_key_expired", (await f.Change()).ErrorCode);
        f.Clock.Value = Now;
        foreach (var source in new[] { receipt.Result with { OrganizationId = Guid.NewGuid() },
            receipt.Result with { ActorId = Guid.NewGuid() }, receipt.Result with { Status = OrganizationStatus.Deleted },
            receipt.Result with { Version = 9 }, receipt.Result with { MembershipVersion = 0 },
            receipt.Result with { ActingCapacity = "MEMBER" }, receipt.Result with { ActorLabel = "\0caption" } })
        {
            f.Store.Receipt = receipt with { Result = source };
            Assert.Equal("organization_lifecycle_source_unavailable", (await f.Change()).ErrorCode);
        }
        foreach (var malformed in new[] { receipt with { Result = null! }, receipt with { Fingerprint = null! },
            receipt with { Key = Guid.NewGuid() }, receipt with { ActorId = Guid.NewGuid() } })
        {
            f.Store.Receipt = malformed;
            Assert.Equal("organization_lifecycle_source_unavailable", (await f.Change()).ErrorCode);
        }
        Assert.Single(f.Store.Sources); Assert.Equal(1, f.Store.Writes);
    }

    [Theory]
    [InlineData("account")] [InlineData("membership")] [InlineData("epoch")]
    [InlineData("role")] [InlineData("parent")] [InlineData("version")]
    public async Task PRD_27_FR007_Post_IO_admission_failure_rolls_back_state_source_and_receipt(string withdrawal)
    {
        var f = new Fixture(); var baseline = f.Store.Scope;
        f.Store.AfterWrite = () =>
        {
            if (withdrawal == "account") f.Auth.Active = false;
            else f.Store.Scope = withdrawal switch
            {
                "membership" => f.Store.Scope! with { MembershipActive = false },
                "epoch" => f.Store.Scope! with { MembershipVersion = 4 },
                "role" => f.Store.Scope! with { Role = OrganizationRole.Admin },
                "parent" => f.Store.Scope! with { Status = OrganizationStatus.Deleting },
                _ => f.Store.Scope! with { Version = 9 },
            };
        };
        var result = await f.Change(); Assert.False(result.Succeeded); Assert.Null(result.Value);
        Assert.Equal(baseline, f.Store.Scope); Assert.Empty(f.Store.Sources); Assert.Null(f.Store.Receipt);
        Assert.Equal(0, f.Unit.Commits); Assert.Equal(1, f.Unit.Rollbacks);
    }

    [Fact]
    public async Task PRD_27_FR007_Expired_session_and_cancelled_command_never_disclose_or_commit()
    {
        var f = new Fixture(); f.Auth.Active = false;
        Assert.Equal("session_unavailable", (await f.Change()).ErrorCode); Assert.Equal(0, f.Store.ScopeReads);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ChangeAsync(Organization, Actor,
            OrganizationStatus.Suspended, 7, Key, "reviewed-change", cancellation.Token));
        Assert.Equal(1, f.Unit.Calls);
        f.Auth.Active = true;
        // Use a fresh cancellation token to exercise rollback after the write.
        using var late = new CancellationTokenSource(); f.Store.AfterWrite = late.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ChangeAsync(Organization, Actor,
            OrganizationStatus.Suspended, 7, Key, "reviewed-change", late.Token));
        Assert.Empty(f.Store.Sources); Assert.Null(f.Store.Receipt); Assert.Equal(OrganizationStatus.Active, f.Store.Scope!.Status);
    }

    [Theory]
    [InlineData(OrganizationOperationalLifecycleWriteResult.VersionConflict, "version_conflict")]
    [InlineData(OrganizationOperationalLifecycleWriteResult.KeyConflict, "idempotency_key_conflict")]
    [InlineData(OrganizationOperationalLifecycleWriteResult.InvalidSource, "organization_lifecycle_source_unavailable")]
    public async Task PRD_27_FR007_Persistence_conflicts_return_no_source_or_acknowledgment(
        OrganizationOperationalLifecycleWriteResult outcome, string error)
    {
        var f = new Fixture(); f.Store.WriteResult = outcome;
        Assert.Equal(error, (await f.Change()).ErrorCode);
        Assert.Empty(f.Store.Sources); Assert.Null(f.Store.Receipt);
        Assert.Equal(OrganizationStatus.Active, f.Store.Scope!.Status); Assert.Equal(0, f.Unit.Commits);
    }

    [Fact]
    public async Task PRD_27_FR007_Recovery_rechecks_current_membership_after_receipt_IO()
    {
        var f = new Fixture(); Assert.True((await f.Change()).Succeeded);
        var source = Assert.Single(f.Store.Sources); var receipt = f.Store.Receipt;
        f.Store.AfterReceipt = () => f.Store.Scope = f.Store.Scope! with { MembershipActive = false };
        var result = await f.Change(); Assert.Equal("organization_not_found", result.ErrorCode); Assert.Null(result.Value);
        Assert.Same(source, Assert.Single(f.Store.Sources)); Assert.Same(receipt, f.Store.Receipt);
        Assert.Equal(1, f.Store.Writes);
    }

    [Fact]
    public async Task PRD_27_FR007_Recovery_rejects_impossible_source_clock_expiry_or_regressed_parent_version()
    {
        var f = new Fixture(); Assert.True((await f.Change()).Succeeded);
        var original = f.Store.Receipt!;
        f.Store.Receipt = original with { ExpiresAt = original.ExpiresAt.AddDays(1) };
        Assert.Equal("organization_lifecycle_source_unavailable", (await f.Change()).ErrorCode);
        f.Store.Receipt = original; f.Clock.Value = Now.AddTicks(-1);
        Assert.Equal("organization_lifecycle_source_unavailable", (await f.Change()).ErrorCode);
        f.Clock.Value = Now; f.Store.Scope = f.Store.Scope! with { Version = 7 };
        Assert.Equal("organization_lifecycle_source_unavailable", (await f.Change()).ErrorCode);
        Assert.Single(f.Store.Sources); Assert.Equal(1, f.Store.Writes);
    }

    [Fact]
    public async Task PRD_27_FR007_Empty_tenant_or_actor_opens_no_owning_transaction()
    {
        var f = new Fixture();
        foreach (var (organization, actor) in new[] { (Guid.Empty, Actor), (Organization, Guid.Empty) })
            Assert.Equal("organization_not_found", (await f.Service.ChangeAsync(organization, actor,
                OrganizationStatus.Suspended, 7, Key, "reviewed-change", TestContext.Current.CancellationToken)).ErrorCode);
        Assert.Equal(0, f.Unit.Calls); Assert.Equal(0, f.Store.ScopeReads);
    }

    [Fact]
    public void PRD_27_FR007_New_statuses_preserve_numeric_values_in_existing_retained_receipts()
    {
        Assert.Equal(OrganizationStatus.Active, System.Text.Json.JsonSerializer.Deserialize<OrganizationStatus>("0"));
        Assert.Equal(OrganizationStatus.Archived, System.Text.Json.JsonSerializer.Deserialize<OrganizationStatus>("1"));
        Assert.Equal(OrganizationStatus.Deleting, System.Text.Json.JsonSerializer.Deserialize<OrganizationStatus>("2"));
        Assert.Equal(OrganizationStatus.Deleted, System.Text.Json.JsonSerializer.Deserialize<OrganizationStatus>("3"));
    }

    [Fact]
    public async Task PRD_27_FR007_Source_and_parent_clocks_share_PostgreSQL_microsecond_precision()
    {
        var f = new Fixture(); f.Clock.Value = Now.AddTicks(7);
        var result = await f.Change(); Assert.True(result.Succeeded);
        Assert.Equal(Now, result.Value!.CreatedAt); Assert.Equal(Now.AddHours(24), f.Store.Receipt!.ExpiresAt);
    }

    [Theory]
    [InlineData(OrganizationStatus.Active)] [InlineData(OrganizationStatus.Suspended)] [InlineData(OrganizationStatus.Deactivated)]
    public async Task PRD_27_FR007_Administrative_review_is_current_and_does_not_write_history(OrganizationStatus status)
    {
        var f = new Fixture(); f.Store.Scope = f.Store.Scope! with { Status = status };
        var result = await f.Service.ReviewAsync(Organization, Actor, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded); Assert.Equal(status, result.Value!.Status); Assert.Equal(7, result.Value.Version);
        Assert.Equal(Actor, result.Value.ActorId); Assert.Equal("ORGANIZATION_OWNER", result.Value.ActingCapacity);
        Assert.Equal(0, f.Store.Writes); Assert.Equal(0, f.Store.ReceiptReads); Assert.Empty(f.Store.Sources);
    }

    [Theory]
    [InlineData("account")] [InlineData("late_account")] [InlineData("membership")]
    [InlineData("epoch")] [InlineData("role")] [InlineData("version")] [InlineData("name")]
    public async Task PRD_27_FR007_Review_withdrawal_or_changed_revision_returns_no_protected_result(string change)
    {
        var f = new Fixture();
        f.Store.AfterScope = () =>
        {
            if (change == "late_account") { if (f.Store.ScopeReads == 2) f.Auth.Active = false; return; }
            if (f.Store.ScopeReads != 1) return;
            if (change == "account") f.Auth.Active = false;
            else f.Store.Scope = change switch
            {
                "membership" => f.Store.Scope! with { MembershipActive = false },
                "epoch" => f.Store.Scope! with { MembershipVersion = 4 },
                "role" => f.Store.Scope! with { Role = OrganizationRole.Admin },
                "name" => f.Store.Scope! with { OrganizationName = "Changed Organization" },
                _ => f.Store.Scope! with { Version = 8 },
            };
        };
        var result = await f.Service.ReviewAsync(Organization, Actor, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Equal(0, f.Store.Writes); Assert.Empty(f.Store.Sources);
        Assert.Equal(change.Contains("account", StringComparison.Ordinal) ? "session_unavailable"
            : change is "version" or "name" ? "version_conflict" : "organization_not_found", result.ErrorCode);
    }

    private sealed class Fixture
    {
        public Store Store { get; } = new(); public Auth Auth { get; } = new(); public Clock Clock { get; } = new();
        public Unit Unit { get; } public OrganizationOperationalLifecycleService Service { get; }
        public Fixture() { Unit = new(Store); Store.Unit = Unit; Service = new(Unit, Store, Auth, Clock); }
        public Task<OrganizationOperation<OrganizationOperationalLifecycleTransition>> Change(
            OrganizationStatus desired = OrganizationStatus.Suspended, long version = 7, Guid? key = null,
            string correlation = "reviewed-change") => Service.ChangeAsync(Organization, Actor, desired, version,
                key ?? Key, correlation, TestContext.Current.CancellationToken);
    }

    private sealed class Clock : IClock { public DateTimeOffset Value { get; set; } = Now; public DateTimeOffset UtcNow => Value; }
    private sealed class Auth : ICommandActorAuthorization
    {
        public bool Active { get; set; } = true;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Assert.Equal(Actor, actorId); return Task.FromResult(Active); }
    }
    private sealed class Unit(Store store) : IOrganizationOperationalLifecycleUnitOfWork
    {
        public bool Inside { get; private set; } public int Calls { get; private set; }
        public int Commits { get; private set; } public int Rollbacks { get; private set; }
        public async Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization, Guid actor,
            Func<Task<OrganizationOperation<T>>> action, CancellationToken ct)
        {
            Assert.Equal(Organization, organization); Assert.Equal(Actor, actor); Calls++;
            var scope = store.Scope; var receipt = store.Receipt; var sources = store.Sources.ToArray(); var committed = false;
            Inside = true;
            try { var result = await action(); committed = result.Succeeded; if (committed) Commits++; return result; }
            finally
            {
                if (!committed) { Rollbacks++; store.Scope = scope; store.Receipt = receipt; store.Sources.Clear(); store.Sources.AddRange(sources); }
                Inside = false;
            }
        }
    }
    private sealed class Store : IOrganizationOperationalLifecycleStore
    {
        public Unit Unit { get; set; } = null!;
        public OrganizationOperationalLifecycleScope? Scope { get; set; } = new(Organization, OrganizationStatus.Active,
            7, Now.AddHours(-1), Actor, "Original administrator", Guid.NewGuid(), 3, OrganizationRole.Owner, true, "Reviewed Organization");
        public OrganizationOperationalLifecycleReceipt? Receipt { get; set; }
        public List<OrganizationOperationalLifecycleTransition> Sources { get; } = [];
        public int ScopeReads { get; private set; } public int ReceiptReads { get; private set; } public int Writes { get; private set; }
        public Action? AfterWrite { get; set; }
        public Action? AfterReceipt { get; set; }
        public Action? AfterScope { get; set; }
        public OrganizationOperationalLifecycleWriteResult WriteResult { get; set; } = OrganizationOperationalLifecycleWriteResult.Written;
        public Task<OrganizationOperationalLifecycleScope?> ReadScopeAsync(Guid organization, Guid actor, CancellationToken ct)
        { Assert.True(Unit.Inside); ct.ThrowIfCancellationRequested(); ScopeReads++; var value = Scope; AfterScope?.Invoke(); return Task.FromResult(value); }
        public Task<OrganizationOperationalLifecycleReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
        { Assert.True(Unit.Inside); ReceiptReads++; AfterReceipt?.Invoke(); return Task.FromResult(Receipt); }
        public Task<OrganizationOperationalLifecycleWriteResult> WriteAsync(long version,
            OrganizationOperationalLifecycleTransition source, OrganizationOperationalLifecycleReceipt receipt, CancellationToken ct)
        {
            Assert.True(Unit.Inside); Assert.Equal(Scope!.Version, version); Writes++;
            if (WriteResult != OrganizationOperationalLifecycleWriteResult.Written) return Task.FromResult(WriteResult);
            Scope = Scope with { Status = source.Status, Version = source.Version, UpdatedAt = source.CreatedAt };
            Sources.Add(source); Receipt = receipt; AfterWrite?.Invoke();
            return Task.FromResult(OrganizationOperationalLifecycleWriteResult.Written);
        }
    }
}
