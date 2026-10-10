using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class ConfigurationIntakeProbe(IOrganizationConfigurationIntakeStore inner, Action? after = null)
        : IOrganizationConfigurationIntakeStore
    {
        public int Reads { get; private set; }
        public async Task<ConfigurationIntakeListSource?> ReadListsAsync(Guid organization, Guid board, string? afterRank, CancellationToken ct)
        {
            Reads++;
            var result = await inner.ReadListsAsync(organization, board, afterRank, ct);
            after?.Invoke();
            return result;
        }
    }

    [Fact]
    public async Task PRD_27_intake_List_pages_are_bounded_ordered_exclusive_and_exhaustible()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Paged intake parent", ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var clock = app.Services.GetRequiredService<IClock>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var board = (await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            var created = await work.CreateBoardAsync(parent.Id, actor, Guid.NewGuid(), "Actual intake Board", null,
                BoardVisibility.Private, "COLOR", "purple", clock.UtcNow, ct);
            for (var index = 0; index < 53; index++)
                await work.CreateListAsync(created.Id, Guid.NewGuid(), $"Actual List {index}", null, clock.UtcNow, ct);
            return OrganizationOperation<Guid>.Success(created.Id);
        }, ct)).Value;
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        var first = await service.ReadIntakeListsAsync(parent.Id, actor, board, null, ct);
        Assert.True(first.Succeeded, first.ErrorCode);
        Assert.Equal(parent.Id, first.Value!.OrganizationId); Assert.Equal(board, first.Value.Board.Id);
        Assert.Equal(50, first.Value.Items.Count); Assert.Equal(first.Value.Items[^1].Rank, first.Value.NextAfterRank);
        Assert.Equal(first.Value.Items.Select(row => row.Rank).Order(StringComparer.Ordinal), first.Value.Items.Select(row => row.Rank));
        var second = await service.ReadIntakeListsAsync(parent.Id, actor, board, first.Value.NextAfterRank, ct);
        Assert.True(second.Succeeded, second.ErrorCode); Assert.Equal(3, second.Value!.Items.Count); Assert.Null(second.Value.NextAfterRank);
        Assert.Equal(53, first.Value.Items.Concat(second.Value.Items).Select(row => row.Id).Distinct().Count());
        Assert.All(second.Value.Items, row => Assert.True(string.CompareOrdinal(row.Rank, first.Value.NextAfterRank) > 0));
        var exhausted = await service.ReadIntakeListsAsync(parent.Id, actor, board, second.Value.Items[^1].Rank, ct);
        Assert.True(exhausted.Succeeded, exhausted.ErrorCode); Assert.Empty(exhausted.Value!.Items); Assert.Null(exhausted.Value.NextAfterRank);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("00000000000000000000000000000x")]
    [InlineData("00000000000000000000000000000١")]
    public async Task PRD_27_intake_cursor_errors_follow_admission_and_never_reach_the_store(string cursor)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Cursor parent", ct);
        var probe = new ConfigurationIntakeProbe(app.Services.GetRequiredService<IOrganizationConfigurationIntakeStore>());
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services, probe);
        var denied = await service.ReadIntakeListsAsync(Guid.NewGuid(), actor, Guid.NewGuid(), cursor, ct);
        Assert.False(denied.Succeeded); Assert.Equal("organization_not_found", denied.ErrorCode); Assert.Null(denied.Value);
        var invalid = await service.ReadIntakeListsAsync(parent.Id, actor, Guid.NewGuid(), cursor, ct);
        Assert.False(invalid.Succeeded); Assert.Equal("invalid_configuration_intake_cursor", invalid.ErrorCode);
        Assert.Null(invalid.Value); Assert.Equal(0, probe.Reads);
    }

    [Fact]
    public async Task PRD_27_intake_List_final_account_admission_loss_withholds_the_private_page()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Final intake admission", ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var clock = app.Services.GetRequiredService<IClock>();
        var board = (await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            var created = await work.CreateBoardAsync(parent.Id, actor, Guid.NewGuid(), "Private intake Board", null,
                BoardVisibility.Private, "COLOR", "purple", clock.UtcNow, ct);
            await work.CreateListAsync(created.Id, Guid.NewGuid(), "Private intake List", null, clock.UtcNow, ct);
            return OrganizationOperation<Guid>.Success(created.Id);
        }, ct)).Value;
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<ICommandActorAuthorization>());
        var probe = new ConfigurationIntakeProbe(app.Services.GetRequiredService<IOrganizationConfigurationIntakeStore>(), () => fence.Denied = true);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services, probe, fence);
        var result = await service.ReadIntakeListsAsync(parent.Id, actor, board, null, ct);
        Assert.False(result.Succeeded); Assert.Equal("organization_not_found", result.ErrorCode); Assert.Null(result.Value); Assert.Equal(1, probe.Reads);
    }
}
