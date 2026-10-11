using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // Contract probe only: this is not a Demo persistence implementation and
    // does not establish atomic rollback/audit behavior of a runtime adapter.
    private sealed class PolicyProbe : IRetentionPolicyStore
    {
        public RecordRetentionPolicy? Current { get; set; }
        public RetentionPolicyReceipt? Receipt { get; set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Action? AfterRead { get; set; }
        public Task<RecordRetentionPolicy?> ReadAsync(Guid organization, Guid policy, CancellationToken ct)
        { Reads++; AfterRead?.Invoke(); return Task.FromResult(Current); }
        public Task<RetentionPolicyReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
        { Reads++; return Task.FromResult(Receipt?.Key == key ? Receipt : null); }
        public Task<RetentionPolicyWriteResult> WriteAsync(long expectedVersion, RetentionPolicyWrite write, CancellationToken ct)
        { Writes++; Current = write.Policy; Receipt = write.Receipt; return Task.FromResult(RetentionPolicyWriteResult.Written); }
    }

    private static RetentionPolicyInput PolicyInput() => new(RetentionRecordClass.Document,
        "contracts", "CA-BC", RetentionAnchor.Closed, 365, RecordDispositionAction.Preserve,
        "Reviewed Organization policy", null);

    [Fact]
    public async Task PRD_70_policy_command_replays_original_acknowledgment_and_refuses_changed_retry_stale_and_scope_move()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Policy command", ct);
        var probe = new PolicyProbe(); var clock = new ConfigurationCommandClock();
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services, probe, clock);
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var input = PolicyInput();
        var first = await service.ChangeAsync(parent.Id, actor, id, input, 0, key, "policy-first", ct);
        Assert.True(first.Succeeded); Assert.Equal(1, first.Value!.Version);
        Assert.Equal(0, first.Value.UpdatedAt.Ticks % 10);
        var firstReceipt = probe.Receipt;
        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        var second = await service.ChangeAsync(parent.Id, actor, id, input with { RetentionDays = 730 }, 1,
            Guid.NewGuid(), "policy-second", ct);
        Assert.True(second.Succeeded); Assert.Equal(2, second.Value!.Version);
        // Restore the retained first receipt while preserving current revision 2.
        probe.Receipt = firstReceipt;
        Assert.Equal(first.Value, (await service.ChangeAsync(parent.Id, actor, id, input, 0, key, "retry-correlation", ct)).Value);
        Assert.Equal("idempotency_key_conflict", (await service.ChangeAsync(parent.Id, actor, id, input, 2, key, "changed-retry", ct)).ErrorCode);
        Assert.Equal("version_conflict", (await service.ChangeAsync(parent.Id, actor, id, input, 0, Guid.NewGuid(), "stale", ct)).ErrorCode);
        Assert.Equal("retention_policy_scope_immutable", (await service.ChangeAsync(parent.Id, actor, id,
            input with { Category = "other" }, 2, Guid.NewGuid(), "scope-move", ct)).ErrorCode);
        clock.UtcNow = first.Value.UpdatedAt.AddHours(24);
        Assert.Equal("idempotency_key_expired", (await service.ChangeAsync(parent.Id, actor, id, input, 0, key, "expired", ct)).ErrorCode);
        Assert.Equal(2, probe.Writes); Assert.Equal(second.Value, probe.Current);
    }

    [Fact]
    public async Task PRD_70_policy_command_admits_owner_before_validation_or_private_source_IO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(foreign);
        var (actor, parent) = await ConfigurationParent(owner, "Owning policy", ct);
        var (outsider, _) = await ConfigurationParent(foreign, "Foreign policy", ct);
        var probe = new PolicyProbe();
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services, probe);
        var refused = await service.ChangeAsync(parent.Id, outsider, Guid.Empty, null, -1, Guid.Empty, "", ct);
        Assert.False(refused.Succeeded); Assert.Equal("organization_not_found", refused.ErrorCode);
        Assert.Equal(0, probe.Reads); Assert.Equal(0, probe.Writes);
        Assert.Equal("invalid_retention_policy_source", (await service.ChangeAsync(parent.Id, actor, Guid.NewGuid(),
            PolicyInput() with { Source = " " }, 0, Guid.NewGuid(), "invalid", ct)).ErrorCode);
        Assert.Equal(0, probe.Reads);
    }

    [Fact]
    public async Task PRD_70_policy_read_and_receipt_refuse_foreign_source_and_changed_receipt_result()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Policy provenance", ct);
        var probe = new PolicyProbe();
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services, probe);
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var input = PolicyInput();
        Assert.True((await service.ChangeAsync(parent.Id, actor, id, input, 0, key, "initial", ct)).Succeeded);
        probe.Current = probe.Current! with { OrganizationId = Guid.NewGuid() };
        Assert.Equal("retention_policy_source_unavailable", (await service.ReadAsync(parent.Id, actor, id, ct)).ErrorCode);
        probe.Receipt = probe.Receipt! with { Result = probe.Receipt.Result with { Source = "Substituted source" } };
        Assert.Equal("retention_policy_source_unavailable", (await service.ChangeAsync(parent.Id, actor, id, input, 0, key, "retry", ct)).ErrorCode);
        Assert.Equal(1, probe.Writes);
    }

    [Fact]
    public async Task PRD_70_policy_read_withdraws_private_result_after_account_authority_changes_during_IO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Policy disclosure", ct);
        var probe = new PolicyProbe();
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<StrataAI.Application.Identity.ICommandActorAuthorization>());
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services, probe, fence);
        var id = Guid.NewGuid();
        Assert.True((await service.ChangeAsync(parent.Id, actor, id, PolicyInput(), 0, Guid.NewGuid(), "initial", ct)).Succeeded);
        probe.AfterRead = () => fence.Denied = true;
        var refused = await service.ReadAsync(parent.Id, actor, id, ct);
        Assert.False(refused.Succeeded); Assert.Equal("organization_not_found", refused.ErrorCode); Assert.Null(refused.Value);
    }

    [Fact]
    public async Task PRD_70_policy_postgres_adapter_refuses_unowned_operations_before_opening_database()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        await using var connections = new StrataAI.Infrastructure.Persistence.PostgresConnectionFactory(
            "Host=127.0.0.1;Port=1;Database=unopened;Username=unopened;Password=unopened;Timeout=1");
        var store = ActivatorUtilities.CreateInstance<StrataAI.Infrastructure.Records.PostgresRetentionPolicyStore>(app.Services, connections);
        var organization = Guid.NewGuid(); var actor = Guid.NewGuid(); var policy = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(organization, policy, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadReceiptAsync(organization, actor, Guid.NewGuid(), ct));
        var at = DateTimeOffset.UtcNow;
        var source = new RecordRetentionPolicy(policy, organization, 1, RetentionRecordClass.Document,
            "contracts", null, RetentionAnchor.Created, null, RecordDispositionAction.Preserve, "Explicit policy", null, at, at);
        var write = new RetentionPolicyWrite(source, actor, Guid.NewGuid(), 1, OrganizationRole.Owner,
            Guid.NewGuid(), "unowned", new(actor, Guid.NewGuid(), new string('A', 64), source, at.AddHours(24)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(0, write, ct));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync(organization, policy, cancelled.Token));
    }
}
