using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class ConfigurationCommandClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class ConfigurationFinalFence(ICommandActorAuthorization inner) : ICommandActorAuthorization
    {
        public bool Denied { get; set; }
        public Task<bool> VerifyAsync(Guid actor, CancellationToken cancellationToken = default) =>
            Denied ? Task.FromResult(false) : inner.VerifyAsync(actor, cancellationToken);
    }

    private sealed class ConfigurationAfterWrite(IOrganizationConfigurationStore inner, Action after) : IOrganizationConfigurationStore
    {
        public Task<bool> IntakeAvailableAsync(Guid organization, Guid? board, Guid? list, CancellationToken ct) => inner.IntakeAvailableAsync(organization, board, list, ct);
        public Task<OrganizationConfigurationRecord?> ReadAsync(Guid organization, CancellationToken ct) => inner.ReadAsync(organization, ct);
        public Task<OrganizationConfigurationReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptAsync(organization, actor, key, ct);
        public Task<IReadOnlyList<OrganizationConfigurationRecord>> ReadHistoryAsync(Guid organization, long? beforeVersion, CancellationToken ct) => inner.ReadHistoryAsync(organization, beforeVersion, ct);
        public Task<IReadOnlyList<OrganizationConfigurationEvent>> ReadEventsAsync(Guid organization, long afterVersion, CancellationToken ct) => inner.ReadEventsAsync(organization, afterVersion, ct);
        public async Task<OrganizationConfigurationWriteResult> WriteAsync(long expectedVersion, OrganizationConfigurationRecord record,
            OrganizationConfigurationReceipt receipt, CancellationToken ct)
        {
            var result = await inner.WriteAsync(expectedVersion, record, receipt, ct);
            if (result == OrganizationConfigurationWriteResult.Written) after();
            return result;
        }
    }

    [Fact]
    public async Task PRD_27_configuration_command_final_admission_loss_rolls_back_history_events_receipt_and_identifier_claim()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Final admission parent", ct);
        var store = app.Services.GetRequiredService<IOrganizationConfigurationStore>();
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<ICommandActorAuthorization>());
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services,
            new ConfigurationAfterWrite(store, () => fence.Denied = true), fence);
        var key = Guid.NewGuid(); var data = new OrganizationConfigurationData("Reviewed name", "CA-BC", "UTC", CorporationIdentifier: "ATOMIC-EXAMPLE");
        var rejected = await service.ChangeAsync(parent.Id, actor, data, 0, key, "final-fence-command", ct);
        Assert.False(rejected.Succeeded); Assert.Equal("organization_not_found", rejected.ErrorCode); Assert.Null(rejected.Value);
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.Null(await store.ReadAsync(parent.Id, ct)); Assert.Empty(await store.ReadHistoryAsync(parent.Id, null, ct));
            Assert.Empty(await store.ReadEventsAsync(parent.Id, 0, ct)); Assert.Null(await store.ReadReceiptAsync(parent.Id, actor, key, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var normal = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        var (_, other) = await ConfigurationParent(owner, "Released identifier parent", ct);
        Assert.True((await normal.ChangeAsync(other.Id, actor, data, 0, Guid.NewGuid(), "reclaim-rolled-back-identifier", ct)).Succeeded);
    }

    [Fact]
    public async Task PRD_27_configuration_intake_rejects_other_tenant_sibling_list_and_archived_board_without_new_revisions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Intake parent", ct);
        var (_, other) = await ConfigurationParent(owner, "Other intake parent", ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var clock = app.Services.GetRequiredService<IClock>();
        async Task<(BoardRecord Board, BoardListRecord List)> Fixture(Guid organization) =>
            (await ConfigurationOwned(unit, organization, actor, null, false, async () =>
            {
                var board = await work.CreateBoardAsync(organization, actor, Guid.NewGuid(), "Reviewed intake", null,
                    BoardVisibility.Private, "COLOR", "purple", clock.UtcNow, ct);
                var list = await work.CreateListAsync(board.Id, Guid.NewGuid(), "Reviewed incoming", null, clock.UtcNow, ct);
                return OrganizationOperation<(BoardRecord, BoardListRecord)>.Success((board, list));
            }, ct)).Value;
        var local = await Fixture(parent.Id); var sibling = await Fixture(parent.Id); var foreign = await Fixture(other.Id);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        var data = new OrganizationConfigurationData("Reviewed name", "CA-BC", "UTC");
        foreach (var invalid in new[]
        {
            data with { IntakeBoardId = foreign.Board.Id, IntakeListId = foreign.List.Id },
            data with { IntakeBoardId = local.Board.Id, IntakeListId = sibling.List.Id },
            data with { IntakeBoardId = local.Board.Id, IntakeListId = foreign.List.Id },
        })
            Assert.Equal("configuration_intake_unavailable", (await service.ChangeAsync(parent.Id, actor, invalid,
                0, Guid.NewGuid(), "substituted-intake", ct)).ErrorCode);
        var valid = data with { IntakeBoardId = local.Board.Id, IntakeListId = local.List.Id };
        var saved = await service.ChangeAsync(parent.Id, actor, valid, 0, Guid.NewGuid(), "reviewed-intake", ct);
        Assert.True(saved.Succeeded, saved.ErrorCode);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.NotNull(await work.SetBoardLifecycleAsync(local.Board.Id, BoardLifecycleState.Active,
                BoardLifecycleState.Archived, local.Board.Version, clock.UtcNow, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.Equal("configuration_intake_unavailable", (await service.ChangeAsync(parent.Id, actor, valid,
            1, Guid.NewGuid(), "archived-intake", ct)).ErrorCode);
        var retained = (await service.ReadHistoryAsync(parent.Id, actor, null, ct)).Value!;
        Assert.Single(retained.Items); Assert.Equal(local.Board.Id, retained.Items[0].Configuration.IntakeBoardId);
        Assert.Equal(local.List.Id, retained.Items[0].Configuration.IntakeListId);
    }

    [Fact]
    public async Task PRD_27_configuration_command_preserves_original_receipt_after_management_timezone_and_jurisdiction_change()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Reviewed configuration", ct);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        var empty = await service.ReadAsync(parent.Id, actor, ct);
        Assert.True(empty.Succeeded); Assert.Equal(0, empty.Value!.Version); Assert.Null(empty.Value.Revision);
        var intent = new OrganizationConfigurationData("Original legal name", "CA-BC", "America/Vancouver", ManagementCompanyName: "Original manager");
        var key = Guid.NewGuid();
        var first = await service.ChangeAsync(parent.Id, actor, intent, 0, key, "first-reviewed-command", ct);
        Assert.True(first.Succeeded, first.ErrorCode);
        var second = await service.ChangeAsync(parent.Id, actor, intent with
        {
            Jurisdiction = "CA-ON", Timezone = "America/Toronto", ManagementCompanyName = "New manager",
        }, 1, Guid.NewGuid(), "second-reviewed-command", ct);
        Assert.True(second.Succeeded, second.ErrorCode);
        var recovered = await service.ChangeAsync(parent.Id, actor, intent, 0, key, "new-correlation", ct);
        Assert.True(recovered.Succeeded); Assert.Equal(first.Value, recovered.Value);
        Assert.Equal(2, (await service.ReadAsync(parent.Id, actor, ct)).Value!.Version);
        var history = (await service.ReadHistoryAsync(parent.Id, actor, null, ct)).Value!;
        Assert.Equal(new long[] { 2, 1 }, history.Items.Select(row => row.Version));
        Assert.Equal("Original manager", history.Items[1].Configuration.ManagementCompanyName);
        Assert.Equal("America/Vancouver", history.Items[1].Configuration.Timezone);
        Assert.Equal("CA-BC", history.Items[1].Configuration.Jurisdiction);
        Assert.Equal(first.Value!.CreatedAt, second.Value!.CreatedAt);
        Assert.NotEqual(first.Value.EventId, second.Value.EventId);
        Assert.Equal(actor, second.Value.ActorId); Assert.Equal(parent.Type, second.Value.OrganizationType);
        Assert.Equal(parent.Version, second.Value.OrganizationVersion);
    }

    [Fact]
    public async Task PRD_27_configuration_command_denies_private_reads_history_and_replay_after_owner_demotion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        var (actor, parent) = await ConfigurationParent(owner, "Private configuration", ct);
        var outsiderActor = await ConfigurationParent(outsider, "Other private configuration", ct);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        var intent = new OrganizationConfigurationData("Protected legal name", "CA-BC", "UTC"); var key = Guid.NewGuid();
        Assert.True((await service.ChangeAsync(parent.Id, actor, intent, 0, key, "protected-command", ct)).Succeeded);
        Assert.Equal("organization_not_found", (await service.ReadAsync(parent.Id, outsiderActor.Actor, ct)).ErrorCode);
        Assert.Equal("organization_not_found", (await service.ReadHistoryAsync(parent.Id, outsiderActor.Actor, -1, ct)).ErrorCode);
        Assert.Equal("organization_not_found", (await service.ChangeAsync(parent.Id, outsiderActor.Actor, null, 0, key, "invalid", ct)).ErrorCode);
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        await ConfigurationOwned(unit, parent.Id, actor, actor, false, async () =>
        {
            await store.AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Member, app.Services.GetRequiredService<IClock>().UtcNow, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.Equal("organization_not_found", (await service.ReadAsync(parent.Id, actor, ct)).ErrorCode);
        Assert.Equal("organization_not_found", (await service.ReadHistoryAsync(parent.Id, actor, null, ct)).ErrorCode);
        Assert.Equal("organization_not_found", (await service.ChangeAsync(parent.Id, actor, intent, 0, key, "replay-after-demotion", ct)).ErrorCode);
    }

    [Fact]
    public async Task PRD_27_configuration_command_expired_key_stays_reserved_and_cannot_write_again()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new ConfigurationCommandClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Expiry parent", ct);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        var intent = new OrganizationConfigurationData("Reviewed legal name", "CA-BC", "UTC"); var key = Guid.NewGuid();
        Assert.True((await service.ChangeAsync(parent.Id, actor, intent, 0, key, "first-command", ct)).Succeeded);
        clock.UtcNow = clock.UtcNow.AddHours(24);
        Assert.Equal("idempotency_key_expired", (await service.ChangeAsync(parent.Id, actor, intent, 0, key, "expired-replay", ct)).ErrorCode);
        Assert.Equal("idempotency_key_expired", (await service.ChangeAsync(parent.Id, actor, intent with { LegalName = "Different intent" }, 1, key, "expired-reuse", ct)).ErrorCode);
        Assert.Single((await service.ReadHistoryAsync(parent.Id, actor, null, ct)).Value!.Items);
        Assert.Equal(1, (await service.ReadAsync(parent.Id, actor, ct)).Value!.Version);
    }

    [Fact]
    public async Task PRD_27_configuration_command_conflicts_do_not_append_revisions_or_replace_original_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Conflict parent", ct);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        var intent = new OrganizationConfigurationData("Reviewed name", "CA-BC", "UTC"); var key = Guid.NewGuid();
        var first = await service.ChangeAsync(parent.Id, actor, intent, 0, key, "first-command", ct);
        Assert.True(first.Succeeded);
        Assert.Equal("version_conflict", (await service.ChangeAsync(parent.Id, actor, intent, 0, Guid.NewGuid(), "stale", ct)).ErrorCode);
        Assert.Equal("idempotency_key_conflict", (await service.ChangeAsync(parent.Id, actor, intent with { LegalName = "Changed" }, 0, key, "different", ct)).ErrorCode);
        Assert.Equal("configuration_intake_unavailable", (await service.ChangeAsync(parent.Id, actor,
            intent with { IntakeBoardId = Guid.NewGuid() }, 1, Guid.NewGuid(), "unavailable-intake", ct)).ErrorCode);
        Assert.Equal(first.Value, (await service.ChangeAsync(parent.Id, actor, intent, 0, key, "recovery", ct)).Value);
        Assert.Single((await service.ReadHistoryAsync(parent.Id, actor, null, ct)).Value!.Items);
    }

    [Fact]
    public async Task PRD_27_configuration_history_uses_bounded_revision_seek_without_skipping_or_repeating()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "History parent", ct);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services);
        for (var version = 0; version < 52; version++)
            Assert.True((await service.ChangeAsync(parent.Id, actor, new("Reviewed name", "CA-BC", "UTC", LotCount: version + 1),
                version, Guid.NewGuid(), "reviewed-history-command", ct)).Succeeded);
        var page = (await service.ReadHistoryAsync(parent.Id, actor, null, ct)).Value!;
        Assert.Equal(50, page.Items.Count); Assert.Equal(3, page.NextBeforeVersion);
        var last = (await service.ReadHistoryAsync(parent.Id, actor, page.NextBeforeVersion, ct)).Value!;
        Assert.Equal(new long[] { 2, 1 }, last.Items.Select(row => row.Version)); Assert.Null(last.NextBeforeVersion);
        Assert.Equal(52, page.Items.Concat(last.Items).Select(row => row.Version).Distinct().Count());
        Assert.Equal("invalid_configuration_cursor", (await service.ReadHistoryAsync(parent.Id, actor, 0, ct)).ErrorCode);
    }
}
