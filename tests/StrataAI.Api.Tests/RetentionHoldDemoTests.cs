using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Records;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private static ApiFactory HoldDemoFactory(HoldScopeProbe scopes) => new(configureServices: services =>
    {
        // Real Demo adapter + rollback participation, fixture scope authorization.
        // Production registration waits for actual typed authorization and parity.
        services.AddSingleton<IRetentionHoldScopeAuthorization>(scopes);
        services.AddSingleton<InMemoryRetentionSourceStore>();
        services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
        services.AddSingleton<InMemoryRetentionHoldStore>();
        services.AddSingleton<IRetentionHoldStore>(p => p.GetRequiredService<InMemoryRetentionHoldStore>());
        services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionHoldStore>());
    });
    private static async Task<Guid> ClassifiedDemoAttachment(IServiceProvider services, Guid organization, Guid actor, string category, CancellationToken ct)
    {
        var work = services.GetRequiredService<IWorkManagementStore>(); var catalog = services.GetRequiredService<InMemoryRetentionSourceStore>();
        var unit = services.GetRequiredService<IOrganizationUnitOfWork>(); var organizations = services.GetRequiredService<IOrganizationStore>();
        var board = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid(); var file = Guid.NewGuid();
        DateTimeOffset Now() => AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
        await ConfigurationOwned(unit, organization, actor, null, false, async () =>
        {
            await work.CreateBoardAsync(organization, actor, board, "Classification fixture", null, BoardVisibility.Private, "COLOR", null, Now(), ct);
            await work.CreateListAsync(board, list, "Classification list", null, Now(), ct);
            await work.CreateCardAsync(list, card, "Private work", null, null, Now(), ct);
            await ((IAttachmentMetadataStore)work).CreateUrlAttachmentAsync(file, organization, card, actor, "Private source", "https://example.test/private", Now(), ct);
            var member = (await organizations.FindMembershipAsync(organization, actor, ct))!;
            Assert.NotNull(await catalog.CaptureAsync(new(organization, RetentionRecordClass.Document, file, RetentionProducerKind.Attachment,
                category, null, 1, 1, actor, member.Id, member.Version, member.Role, Guid.NewGuid(), "fixture-classification", Now(),
                (await catalog.ReadAsync(organization, RetentionProducerKind.Attachment, file, ct))!), ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        return file;
    }
    private sealed class HoldAfterWrite(IRetentionHoldStore inner, Action after) : IRetentionHoldStore
    {
        public Task<RetentionHoldCandidates> ReadApplicableAsync(RetentionRecord record, CancellationToken ct) => inner.ReadApplicableAsync(record, ct);
        public Task<RetentionHoldReference?> ReadReferenceAsync(Guid organization, Guid hold, CancellationToken ct) => inner.ReadReferenceAsync(organization, hold, ct);
        public Task<RetentionHoldReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptReferenceAsync(organization, actor, key, ct);
        public Task<RetentionHold?> ReadAsync(Guid organization, Guid hold, CancellationToken ct) => inner.ReadAsync(organization, hold, ct);
        public Task<IReadOnlyList<RetentionHoldRevision>> ReadHistoryAsync(Guid organization, Guid hold, CancellationToken ct) => inner.ReadHistoryAsync(organization, hold, ct);
        public Task<RetentionHoldReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptAsync(organization, actor, key, ct);
        public async Task<RetentionHoldWriteResult> WriteAsync(long expectedVersion, RetentionHoldWrite write, CancellationToken ct)
        { var result = await inner.WriteAsync(expectedVersion, write, ct); if (result == RetentionHoldWriteResult.Written) after(); return result; }
    }

    [Fact]
    public async Task PRD_70_hold_Demo_storage_rejects_rewritten_application_and_forged_actor_receipt_provenance()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold write guard", ct);
        var store = app.Services.GetRequiredService<InMemoryRetentionHoldStore>();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services); var id = Guid.NewGuid();
        var applied = await service.ApplyAsync(parent.Id, actor, id, new(parent.Id, RetentionRecordClass.Document, "contracts", null), "Original reason", Guid.NewGuid(), "apply", ct);
        Assert.True(applied.Succeeded);
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            var original = store.ReadHistory(parent.Id, id, ct).Single().Source;
            var now = DateTimeOffset.UtcNow; var at = now.AddTicks(-(now.Ticks % 10));
            var released = RetentionHoldLifecycle.Release(parent.Id, applied.Value!, 1, actor, at).Hold!;
            var write = original with { Hold = released, EventId = Guid.NewGuid(), CorrelationId = "guard",
                Receipt = new(actor, Guid.NewGuid(), new string('A', 64), released, at.AddHours(24)) };
            foreach (var changed in new[] { released with { Reason = "Rewritten reason" }, released with { Category = "other" },
                released with { AppliedBy = Guid.NewGuid() }, released with { AppliedAt = released.AppliedAt.AddTicks(-10) } })
                Assert.Equal(RetentionHoldWriteResult.SourceUnavailable, await store.WriteAsync(1, write with { Hold = changed, Receipt = write.Receipt with { Result = changed } }, ct));
            foreach (var forged in new[] { write with { MembershipId = Guid.NewGuid() }, write with { MembershipVersion = write.MembershipVersion + 1 },
                write with { Receipt = write.Receipt with { ActorId = Guid.NewGuid() } }, write with { Receipt = write.Receipt with { ExpiresAt = at.AddHours(23) } } })
                Assert.Equal(RetentionHoldWriteResult.SourceUnavailable, await store.WriteAsync(1, forged, ct));
            scopes.Allowed = false;
            Assert.Equal(RetentionHoldWriteResult.SourceUnavailable, await store.WriteAsync(1, write, ct)); scopes.Allowed = true;
            Assert.Equal(applied.Value, await store.ReadAsync(parent.Id, id, ct)); Assert.Single(store.ReadHistory(parent.Id, id, ct));
            Assert.Null(await store.ReadReceiptAsync(parent.Id, actor, write.Receipt.Key, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.Single(((InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>()).AuditSnapshot(parent.Id), a => a.EntityType == "RETENTION_HOLD");
    }

    [Fact]
    public async Task PRD_70_hold_Demo_persists_original_application_release_recovery_attribution_and_safe_audit()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Demo hold", ct);
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var store = app.Services.GetRequiredService<InMemoryRetentionHoldStore>();
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var record = await ClassifiedDemoAttachment(app.Services, parent.Id, actor, "private.category", ct);
        var applied = await service.ApplyAsync(parent.Id, actor, id, new(parent.Id, RetentionRecordClass.Document, "private.category", record), "Private hold reason", key, "apply", ct);
        Assert.True(applied.Succeeded); Assert.Equal(applied.Value, (await service.ReadAsync(parent.Id, actor, id, ct)).Value);
        var releaseKeys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var races = await Task.WhenAll(releaseKeys.Select(k => service.ReleaseAsync(parent.Id, actor, id, 1, k, "release race", ct)));
        Assert.Single(races, r => r.Succeeded); Assert.Single(races, r => r.ErrorCode == "version_conflict");
        var winner = Array.FindIndex(races, r => r.Succeeded); var released = races[winner].Value!;
        Assert.Equal(released, (await service.ReadAsync(parent.Id, actor, id, ct)).Value);
        Assert.Equal(applied.Value, (await service.ApplyAsync(parent.Id, actor, id, new(parent.Id, RetentionRecordClass.Document, "private.category", record), "Private hold reason", key, "old apply", ct)).Value);
        Assert.Equal(released, (await service.ReleaseAsync(parent.Id, actor, id, 1, releaseKeys[winner], "old release", ct)).Value);
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, () =>
        {
            var history = store.ReadHistory(parent.Id, id, ct); Assert.Equal(2, history.Count);
            Assert.Equal(applied.Value, history[0].Source.Hold); Assert.Equal(released, history[1].Source.Hold);
            Assert.All(history, r => { Assert.Equal(actor, r.Source.ActorId); Assert.Equal(OrganizationRole.Owner, r.Source.ActingCapacity); Assert.False(string.IsNullOrWhiteSpace(r.ActorLabel)); });
            return Task.FromResult(OrganizationOperation<bool>.Success(true));
        }, ct);
        var audits = ((InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>()).AuditSnapshot(parent.Id)
            .Where(a => a.EntityType == "RETENTION_HOLD").ToArray();
        Assert.Equal(2, audits.Length);
        Assert.Contains(audits, a => a.EventType == "RETENTION_HOLD_APPLIED" && a.HoldMetadata!.Version == 1 && a.HoldMetadata.Status == "ACTIVE");
        Assert.Contains(audits, a => a.EventType == "RETENTION_HOLD_RELEASED" && a.HoldMetadata!.Version == 2 && a.HoldMetadata.Status == "RELEASED");
        Assert.All(audits, a => { Assert.Equal("RECORD", a.HoldMetadata!.ScopeKind); Assert.Equal("ORGANIZATION_OWNER", a.HoldMetadata.ActingCapacity); Assert.Null(a.RetentionMetadata); });
        var serialized = System.Text.Json.JsonSerializer.Serialize(audits);
        Assert.DoesNotContain("Private hold reason", serialized); Assert.DoesNotContain("private.category", serialized); Assert.DoesNotContain(record.ToString(), serialized);
    }

    [Fact]
    public async Task PRD_70_hold_Demo_scope_withdrawal_rolls_back_header_history_receipt_audit_and_reclaims_key()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold rollback", ct);
        var store = app.Services.GetRequiredService<InMemoryRetentionHoldStore>();
        var failing = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, new HoldAfterWrite(store, () => scopes.Allowed = false));
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var holdScope = new RetentionHoldScope(parent.Id, null, null, null);
        var refused = await failing.ApplyAsync(parent.Id, actor, id, holdScope, "Private reason", key, "rollback", ct);
        Assert.False(refused.Succeeded); Assert.Null(refused.Value); Assert.Equal("retention_hold_not_found", refused.ErrorCode);
        scopes.Allowed = true;
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            Assert.Null(await store.ReadAsync(parent.Id, id, ct)); Assert.Empty(store.ReadHistory(parent.Id, id, ct));
            Assert.Null(await store.ReadReceiptAsync(parent.Id, actor, key, ct)); return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.DoesNotContain(((InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>()).AuditSnapshot(parent.Id), a => a.EntityType == "RETENTION_HOLD");
        var normal = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        Assert.True((await normal.ApplyAsync(parent.Id, actor, id, holdScope, "Private reason", key, "reclaim", ct)).Succeeded);
    }

    [Fact]
    public async Task PRD_70_hold_Demo_canceled_release_restores_active_hold_and_rejects_unowned_and_foreign_reads()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold cancel", ct);
        var store = app.Services.GetRequiredService<InMemoryRetentionHoldStore>();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var id = Guid.NewGuid(); var applied = await service.ApplyAsync(parent.Id, actor, id, new(parent.Id, null, null, null), "Reason", Guid.NewGuid(), "apply", ct);
        Assert.True(applied.Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(parent.Id, id, ct));
        using var canceled = new CancellationTokenSource(); var key = Guid.NewGuid();
        var failing = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services, new HoldAfterWrite(store, canceled.Cancel));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => failing.ReleaseAsync(parent.Id, actor, id, 1, key, "cancel", canceled.Token));
        Assert.Equal(applied.Value, (await service.ReadAsync(parent.Id, actor, id, ct)).Value);
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            Assert.Single(store.ReadHistory(parent.Id, id, ct)); Assert.Null(await store.ReadReceiptAsync(parent.Id, actor, key, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(Guid.NewGuid(), id, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.Single(((InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>()).AuditSnapshot(parent.Id), a => a.EntityType == "RETENTION_HOLD");
        Assert.True((await service.ReleaseAsync(parent.Id, actor, id, 1, key, "reclaim release", ct)).Succeeded);
    }
}
