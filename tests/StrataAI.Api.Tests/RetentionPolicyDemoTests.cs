using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Records;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class RetentionAfterWrite(IRetentionPolicyStore inner, Action after) : IRetentionPolicyStore
    {
        public Task<RecordRetentionPolicy?> ReadAsync(Guid organization, Guid policy, CancellationToken ct) => inner.ReadAsync(organization, policy, ct);
        public Task<RetentionPolicyReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptAsync(organization, actor, key, ct);
        public async Task<RetentionPolicyWriteResult> WriteAsync(long expectedVersion, RetentionPolicyWrite write, CancellationToken ct)
        { var result = await inner.WriteAsync(expectedVersion, write, ct); if (result == RetentionPolicyWriteResult.Written) after(); return result; }
    }

    [Fact]
    public async Task PRD_70_policy_Demo_persists_original_retries_retained_attribution_and_serializes_concurrent_writers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Demo policy", ct);
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services);
        var store = app.Services.GetRequiredService<InMemoryRetentionPolicyStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var input = PolicyInput() with { Source = "Private retention source", Notes = "Private retention notes" };
        var first = await service.ChangeAsync(parent.Id, actor, id, input, 0, key, new string('c', 256), ct);
        Assert.True(first.Succeeded); Assert.Equal(first.Value, (await service.ReadAsync(parent.Id, actor, id, ct)).Value);
        var races = await Task.WhenAll(
            service.ChangeAsync(parent.Id, actor, id, input with { RetentionDays = 730 }, 1, Guid.NewGuid(), "race-one", ct),
            service.ChangeAsync(parent.Id, actor, id, input with { RetentionDays = 1095 }, 1, Guid.NewGuid(), "race-two", ct));
        Assert.Single(races, x => x.Succeeded); Assert.Single(races, x => x.ErrorCode == "version_conflict");
        Assert.Equal(first.Value, (await service.ChangeAsync(parent.Id, actor, id, input, 0, key, "original-retry", ct)).Value);
        Assert.Equal("retention_policy_scope_unavailable", (await service.ChangeAsync(parent.Id, actor, Guid.NewGuid(),
            input with { Category = input.Category.ToUpperInvariant(), Jurisdiction = "ca-bc" }, 0, Guid.NewGuid(), "duplicate", ct)).ErrorCode);
        DemoRetentionPolicyRevision? historical = null;
        await ConfigurationOwned(unit, parent.Id, actor, null, false, () =>
        {
            var rows = store.ReadHistory(parent.Id, id, ct); Assert.Equal(2, rows.Count); historical = rows[0];
            Assert.Equal(first.Value, rows[0].Source.Policy); Assert.Equal(OrganizationRole.Owner, rows[0].Source.ActingCapacity);
            return Task.FromResult(OrganizationOperation<bool>.Success(true));
        }, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            await organizations.AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
            Assert.True((await organizations.FindMembershipAsync(parent.Id, actor, ct))!.Version > historical!.Source.MembershipVersion);
            Assert.Equal(historical, store.ReadHistory(parent.Id, id, ct)[0]);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var work = (InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>();
        var audits = work.AuditSnapshot(parent.Id).Where(x => x.EventType == "RECORD_RETENTION_POLICY_CHANGED").ToArray();
        Assert.Equal(2, audits.Length);
        Assert.Equal(new long[] { 1, 2 }, audits.Select(x => x.RetentionMetadata!.Version).Order());
        Assert.All(audits, audit =>
        {
            Assert.Equal("DOCUMENT", audit.RetentionMetadata!.RecordClass);
            Assert.Equal("PRESERVE", audit.RetentionMetadata.Action);
            Assert.Equal("ORGANIZATION_OWNER", audit.RetentionMetadata.ActingCapacity);
        });
        Assert.DoesNotContain("Private retention", System.Text.Json.JsonSerializer.Serialize(audits));
    }

    [Fact]
    public async Task PRD_70_policy_Demo_final_authority_failure_rolls_back_history_receipt_audit_and_namespace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Demo rollback", ct);
        var store = app.Services.GetRequiredService<InMemoryRetentionPolicyStore>();
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<ICommandActorAuthorization>());
        var failing = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services,
            new RetentionAfterWrite(store, () => fence.Denied = true), fence);
        var id = Guid.NewGuid(); var key = Guid.NewGuid();
        var result = await failing.ChangeAsync(parent.Id, actor, id, PolicyInput(), 0, key, "rollback", ct);
        Assert.False(result.Succeeded); Assert.Equal("organization_not_found", result.ErrorCode);
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            Assert.Null(await store.ReadAsync(parent.Id, id, ct)); Assert.Empty(store.ReadHistory(parent.Id, id, ct));
            Assert.Null(await store.ReadReceiptAsync(parent.Id, actor, key, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var work = (InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>();
        Assert.DoesNotContain(work.AuditSnapshot(parent.Id), x => x.EventType == "RECORD_RETENTION_POLICY_CHANGED");
        var normal = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services);
        Assert.True((await normal.ChangeAsync(parent.Id, actor, Guid.NewGuid(), PolicyInput(), 0, key, "reclaim", ct)).Succeeded);
    }

    [Fact]
    public async Task PRD_70_policy_Demo_cancellation_after_write_rolls_back_and_unowned_reads_are_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Demo cancellation", ct);
        var store = app.Services.GetRequiredService<InMemoryRetentionPolicyStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(parent.Id, Guid.NewGuid(), ct));
        using var cancelled = new CancellationTokenSource();
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services, new RetentionAfterWrite(store, cancelled.Cancel));
        var id = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ChangeAsync(parent.Id, actor, id,
            PolicyInput(), 0, Guid.NewGuid(), "cancel-after-write", cancelled.Token));
        var normal = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services);
        Assert.Null((await normal.ReadAsync(parent.Id, actor, id, ct)).Value);
        var work = (InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>();
        Assert.DoesNotContain(work.AuditSnapshot(parent.Id), x => x.EventType == "RECORD_RETENTION_POLICY_CHANGED");
    }
}
