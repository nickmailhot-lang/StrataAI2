using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Records;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_70_hold_lookup_reads_current_Organization_class_category_and_record_scopes_without_history_substitution()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Hold lookup", ct);
        var (_, other) = await ConfigurationParent(owner, "Other hold lookup", ct);
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var store = app.Services.GetRequiredService<InMemoryRetentionHoldStore>();
        var id = await ClassifiedDemoAttachment(app.Services, parent.Id, actor, "contracts", ct);
        var excludedId = await ClassifiedDemoAttachment(app.Services, parent.Id, actor, "contracts", ct); var at = DateTimeOffset.UtcNow;
        var record = new RetentionRecord(id, parent.Id, 1, RetentionRecordClass.Document, "contracts", null, at, null, null, null, false);
        var included = new List<RetentionHold>();
        foreach (var scope in new RetentionHoldScope[] { new(parent.Id, null, null, null), new(parent.Id, record.RecordClass, null, null),
            new(parent.Id, record.RecordClass, "CONTRACTS", null), new(parent.Id, record.RecordClass, null, id) })
        {
            var hold = await service.ApplyAsync(parent.Id, actor, Guid.NewGuid(), scope, "Explicit reason", Guid.NewGuid(), "match", ct);
            Assert.True(hold.Succeeded); included.Add(hold.Value!);
        }
        foreach (var scope in new RetentionHoldScope[] { new(parent.Id, RetentionRecordClass.Governance, null, null),
            new(parent.Id, record.RecordClass, "other", null), new(parent.Id, record.RecordClass, null, excludedId), new(other.Id, null, null, null) })
            Assert.True((await service.ApplyAsync(scope.OrganizationId, actor, Guid.NewGuid(), scope, "Excluded reason", Guid.NewGuid(), "exclude", ct)).Succeeded);
        var released = await service.ReleaseAsync(parent.Id, actor, included[0].Id, 1, Guid.NewGuid(), "release", ct);
        Assert.True(released.Succeeded); included[0] = released.Value!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadApplicableAsync(record, ct));
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            var current = await store.ReadApplicableAsync(record, ct);
            Assert.True(current.ScopeComplete); Assert.Equal(record, current.Record);
            Assert.Equal(included.OrderBy(h => h.Id), current.Items); Assert.Contains(released.Value!, current.Items);
            Assert.DoesNotContain(current.Items, h => h.Id == released.Value!.Id && h.Status == RetentionHoldStatus.Active);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadApplicableAsync(record with { OrganizationId = other.Id }, ct));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadApplicableAsync(record with { Id = Guid.Empty }, ct));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadApplicableAsync(record with { CreatedAt = DateTimeOffset.UtcNow.AddDays(1) }, ct));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadApplicableAsync(record, canceled.Token));
            var now = DateTimeOffset.UtcNow;
            var policy = new RecordRetentionPolicy(Guid.NewGuid(), parent.Id, 1, record.RecordClass, record.Category, null,
                RetentionAnchor.Created, 0, RecordDispositionAction.Delete, "Reviewed source", null, at, at);
            var selection = RetentionPolicies.Select(record, [policy], true, now);
            Assert.Equal(DispositionReason.ActiveHold, current.Evaluate(selection, record, [policy], true, now).Reason);
            Assert.Equal(DispositionReason.InvalidSource, current.Evaluate(selection, record with { Version = 2 }, [policy], true, now).Reason);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
    }

    [Fact]
    public async Task PRD_70_hold_lookup_exposes_overflow_as_incomplete_instead_of_disposition_permission()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Bounded hold lookup", ct);
        var store = app.Services.GetRequiredService<InMemoryRetentionHoldStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            var member = (await organizations.FindMembershipAsync(parent.Id, actor, ct))!;
            var now = DateTimeOffset.UtcNow; var at = now.AddTicks(-(now.Ticks % 10));
            var record = new RetentionRecord(Guid.NewGuid(), parent.Id, 1, RetentionRecordClass.Document, "contracts", null, at, null, null, null, false);
            var empty = await store.ReadApplicableAsync(record, ct); Assert.True(empty.ScopeComplete); Assert.Empty(empty.Items);
            for (var index = 0; index <= RetentionHoldCandidates.MaximumSynchronousCandidates; index++)
            {
                if (index == RetentionHoldCandidates.MaximumSynchronousCandidates)
                {
                    var exact = await store.ReadApplicableAsync(record, ct); Assert.True(exact.ScopeComplete);
                    Assert.Equal(index, exact.Items.Count);
                }
                var hold = RetentionHoldLifecycle.Apply(Guid.NewGuid(), new(parent.Id, null, null, null), actor, "Explicit reason", at).Hold!;
                var write = new RetentionHoldWrite(hold, actor, member.Id, member.Version, member.Role, Guid.NewGuid(), "bulk fixture",
                    new(actor, Guid.NewGuid(), new string('A', 64), hold, at.AddHours(24)));
                Assert.Equal(RetentionHoldWriteResult.Written, await store.WriteAsync(0, write, ct));
            }
            var overflow = await store.ReadApplicableAsync(record, ct); Assert.False(overflow.ScopeComplete);
            Assert.Equal(RetentionHoldCandidates.MaximumSynchronousCandidates + 1, overflow.Items.Count);
            var policy = new RecordRetentionPolicy(Guid.NewGuid(), parent.Id, 1, record.RecordClass, record.Category, null,
                RetentionAnchor.Created, 0, RecordDispositionAction.Delete, "Explicit source", null, at, at);
            now = DateTimeOffset.UtcNow; var selection = RetentionPolicies.Select(record, [policy], true, now);
            var decision = overflow.Evaluate(selection, record, [policy], true, now);
            Assert.False(decision.CanDispose); Assert.Equal(RecordDispositionAction.Preserve, decision.Action);
            Assert.Equal(DispositionReason.IncompleteHoldScope, decision.Reason);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
    }
}
