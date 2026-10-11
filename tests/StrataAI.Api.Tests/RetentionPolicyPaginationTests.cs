using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_70_policy_directory_seeks_bounded_pages_without_other_Organization_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Policy directory", ct);
        var (_, other) = await ConfigurationParent(owner, "Other policy directory", ct);
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services);
        var identities = new List<Guid>();
        for (var index = 0; index < 52; index++)
        {
            var id = Guid.NewGuid(); identities.Add(id);
            Assert.True((await service.ChangeAsync(parent.Id, actor, id, PolicyInput() with { Category = "category." + index },
                0, Guid.NewGuid(), "directory", ct)).Succeeded);
        }
        var foreign = Guid.NewGuid();
        Assert.True((await service.ChangeAsync(other.Id, actor, foreign, PolicyInput(), 0, Guid.NewGuid(), "foreign", ct)).Succeeded);
        var page = await service.ReadPoliciesAsync(parent.Id, actor, null, ct);
        Assert.True(page.Succeeded); Assert.Equal(50, page.Value!.Items.Count); Assert.NotNull(page.Value.NextAfter);
        var last = await service.ReadPoliciesAsync(parent.Id, actor, page.Value.NextAfter, ct);
        Assert.True(last.Succeeded); Assert.Equal(2, last.Value!.Items.Count); Assert.Null(last.Value.NextAfter);
        Assert.Equal(identities.Order(), page.Value.Items.Concat(last.Value.Items).Select(x => x.Id));
        Assert.DoesNotContain(page.Value.Items.Concat(last.Value.Items), x => x.Id == foreign || x.OrganizationId != parent.Id);
        Assert.Equal("invalid_retention_policy_cursor", (await service.ReadPoliciesAsync(parent.Id, actor, Guid.Empty, ct)).ErrorCode);
    }

    [Fact]
    public async Task PRD_70_policy_history_seeks_retained_versions_with_original_attribution_and_empty_boundary()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Policy history", ct);
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services);
        var id = Guid.NewGuid();
        for (var version = 0; version < 53; version++)
            Assert.True((await service.ChangeAsync(parent.Id, actor, id, PolicyInput() with { RetentionDays = version },
                version, Guid.NewGuid(), "history", ct)).Succeeded);
        var page = await service.ReadHistoryAsync(parent.Id, actor, id, null, ct);
        Assert.True(page.Succeeded); Assert.Equal(50, page.Value!.Items.Count); Assert.Equal(4, page.Value.NextBeforeVersion);
        Assert.Equal(Enumerable.Range(4, 50).Reverse().Select(x => (long)x), page.Value.Items.Select(x => x.Policy.Version));
        var last = await service.ReadHistoryAsync(parent.Id, actor, id, page.Value.NextBeforeVersion, ct);
        Assert.True(last.Succeeded); Assert.Equal(new long[] { 3, 2, 1 }, last.Value!.Items.Select(x => x.Policy.Version));
        Assert.Null(last.Value.NextBeforeVersion);
        Assert.All(page.Value.Items.Concat(last.Value.Items), x =>
        {
            Assert.Equal(actor, x.ActorId); Assert.Equal(OrganizationRole.Owner, x.ActingCapacity);
            Assert.NotEqual(Guid.Empty, x.MembershipId); Assert.NotEqual(Guid.Empty, x.EventId); Assert.NotEmpty(x.ActorLabel);
        });
        var empty = await service.ReadHistoryAsync(parent.Id, actor, id, 1, ct);
        Assert.True(empty.Succeeded); Assert.Empty(empty.Value!.Items); Assert.Null(empty.Value.NextBeforeVersion);
        Assert.Equal("invalid_retention_policy_cursor", (await service.ReadHistoryAsync(parent.Id, actor, id, 0, ct)).ErrorCode);
        Assert.Equal("retention_policy_not_found", (await service.ReadHistoryAsync(parent.Id, actor, Guid.NewGuid(), null, ct)).ErrorCode);
    }

    [Fact]
    public async Task PRD_70_policy_pages_refuse_duplicate_foreign_incomplete_or_substituted_sources_and_withdraw_after_IO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Policy page source", ct);
        var probe = new PolicyProbe();
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<StrataAI.Application.Identity.ICommandActorAuthorization>());
        var service = ActivatorUtilities.CreateInstance<RetentionPolicyService>(app.Services, probe, fence);
        var id = Guid.NewGuid();
        var first = await service.ChangeAsync(parent.Id, actor, id, PolicyInput(), 0, Guid.NewGuid(), "page-source", ct);
        Assert.True(first.Succeeded);
        probe.PolicyRows = [first.Value!, first.Value!];
        Assert.Equal("retention_policy_source_unavailable", (await service.ReadPoliciesAsync(parent.Id, actor, null, ct)).ErrorCode);
        probe.PolicyRows = [first.Value! with { OrganizationId = Guid.NewGuid() }];
        Assert.Equal("retention_policy_source_unavailable", (await service.ReadPoliciesAsync(parent.Id, actor, null, ct)).ErrorCode);
        probe.HistoryRows = [];
        Assert.Equal("retention_policy_source_unavailable", (await service.ReadHistoryAsync(parent.Id, actor, id, null, ct)).ErrorCode);
        probe.HistoryRows = [new(first.Value! with { Source = "Substituted history" }, actor, "Historical actor",
            Guid.NewGuid(), 1, OrganizationRole.Owner, Guid.NewGuid())];
        Assert.Equal("retention_policy_source_unavailable", (await service.ReadHistoryAsync(parent.Id, actor, id, null, ct)).ErrorCode);
        probe.PolicyRows = [first.Value!]; probe.AfterRead = () => fence.Denied = true;
        var withdrawn = await service.ReadPoliciesAsync(parent.Id, actor, null, ct);
        Assert.False(withdrawn.Succeeded); Assert.Equal("organization_not_found", withdrawn.ErrorCode); Assert.Null(withdrawn.Value);
    }
}
