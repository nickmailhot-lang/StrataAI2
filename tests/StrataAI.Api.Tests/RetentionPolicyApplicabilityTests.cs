using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_70_applicable_policy_reader_returns_complete_current_namespace_under_owning_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Applicable policy", ct);
        var (_, other) = await ConfigurationParent(owner, "Other policy scope", ct);
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services);
        var store = app.Services.GetRequiredService<IRetentionPolicyStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var globalId = Guid.NewGuid(); var specificId = Guid.NewGuid();
        var input = PolicyInput() with { Jurisdiction = null };
        var global = await service.ChangeAsync(parent.Id, actor, globalId, input, 0, Guid.NewGuid(), "global", ct);
        Assert.True(global.Succeeded);
        var specific = await service.ChangeAsync(parent.Id, actor, specificId, input with { Jurisdiction = "Québec" }, 0, Guid.NewGuid(), "specific", ct);
        Assert.True(specific.Succeeded);
        foreach (var excluded in new[] { input with { Jurisdiction = "CA-BC" }, input with { Category = "other" },
            input with { RecordClass = RetentionRecordClass.Governance } })
            Assert.True((await service.ChangeAsync(parent.Id, actor, Guid.NewGuid(), excluded, 0, Guid.NewGuid(), "excluded", ct)).Succeeded);
        Assert.True((await service.ChangeAsync(other.Id, actor, Guid.NewGuid(), input, 0, Guid.NewGuid(), "foreign", ct)).Succeeded);
        var updated = await service.ChangeAsync(parent.Id, actor, globalId, input with { Source = "New reviewed source", RetentionDays = 730 }, 1, Guid.NewGuid(), "update", ct);
        Assert.True(updated.Succeeded);
        var scope = new RetentionPolicyApplicability(parent.Id, RetentionRecordClass.Document, "CONTRACTS", "QUÉBEC");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadApplicableAsync(scope, ct));
        var read = await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            var rows = await store.ReadApplicableAsync(scope, ct);
            Assert.Equal(2, rows.Count);
            Assert.Contains(updated.Value!, rows); Assert.Contains(specific.Value!, rows); Assert.DoesNotContain(global.Value!, rows);
            Assert.Equal(rows.OrderBy(p => p.Id), rows);
            Assert.Equal(updated.Value, Assert.Single(await store.ReadApplicableAsync(scope with { Jurisdiction = null }, ct)));
            Assert.Equal(updated.Value, Assert.Single(await store.ReadApplicableAsync(scope with { Jurisdiction = "unconfigured" }, ct)));
            Assert.Empty(await store.ReadApplicableAsync(scope with { Category = "absent" }, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadApplicableAsync(scope with { OrganizationId = other.Id }, ct));
            foreach (var invalid in new[] { scope with { Category = "bad category" }, scope with { RecordClass = (RetentionRecordClass)99 }, scope with { Jurisdiction = " QUÉBEC" } })
                await Assert.ThrowsAsync<ArgumentException>(() => store.ReadApplicableAsync(invalid, ct));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadApplicableAsync(scope, canceled.Token));
            var now = DateTimeOffset.UtcNow;
            var record = new RetentionRecord(Guid.NewGuid(), parent.Id, 1, scope.RecordClass, scope.Category, scope.Jurisdiction, now, null, null, null, false);
            Assert.Equal(RetentionPolicySelectionReason.ChoiceRequired, RetentionPolicies.Select(record, rows, true, now).Reason);
            Assert.Equal(updated.Value, RetentionPolicies.Select(record, rows, true, now, new(parent.Id, globalId, 2)).Policy);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.True(read.Succeeded);
    }
}
