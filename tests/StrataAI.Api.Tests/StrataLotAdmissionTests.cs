using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Property;
using StrataAI.Domain.Property;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    private sealed class UnitAfterWrite(IStrataLotStore inner, Action after) : IStrataLotStore {
        public Task<IReadOnlyList<StrataLot>> ListAsync(Guid org, Guid? cursor, CancellationToken ct) => inner.ListAsync(org, cursor, ct);
        public Task<StrataLot?> ReadAsync(Guid org, Guid id, CancellationToken ct) => inner.ReadAsync(org, id, ct);
        public Task<IReadOnlyList<StrataLotRevision>> HistoryAsync(Guid org, Guid id, long? before, CancellationToken ct) => inner.HistoryAsync(org, id, before, ct);
        public Task<StrataLotReceipt?> ReceiptAsync(Guid org, Guid actor, Guid key, CancellationToken ct) => inner.ReceiptAsync(org, actor, key, ct);
        public async Task<StrataLotWriteResult> WriteAsync(StrataLotRevision revision, StrataLotReceipt receipt, long version, CancellationToken ct) {
            var result = await inner.WriteAsync(revision, receipt, version, ct); if (result == StrataLotWriteResult.Written) after(); return result;
        }
    }
    [Fact]
    public async Task PRD_28_unit_final_authority_loss_rolls_back_directory_history_and_retry_receipt() {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Unit rollback", ct);
        var store = app.Services.GetRequiredService<IStrataLotStore>();
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<ICommandActorAuthorization>());
        var service = ActivatorUtilities.CreateInstance<StrataLotService>(app.Services, new UnitAfterWrite(store, () => fence.Denied = true), fence);
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var data = new StrataLotData("48", "SL48", null, null, null, null, "ACTIVE");
        var result = await service.ChangeAsync(parent.Id, actor, id, data, 0, key, "unit-final-fence", ct);
        Assert.False(result.Succeeded);
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        await unit.ExecuteAsync(parent.Id, actor, null, false, async () => {
            Assert.Null(await store.ReadAsync(parent.Id, id, ct));
            Assert.Empty(await store.HistoryAsync(parent.Id, id, null, ct));
            Assert.Null(await store.ReceiptAsync(parent.Id, actor, key, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var normal = app.Services.GetRequiredService<StrataLotService>();
        Assert.True((await normal.ChangeAsync(parent.Id, actor, id, data, 0, key, "unit-recover-after-rollback", ct)).Succeeded);
    }
    [Fact]
    public async Task PRD_28_unit_administration_denies_members_and_revoked_admin_original_receipts() {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var (_, parent) = await ConfigurationParent(owner, "Unit roles", ct);
        var actor = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var root = $"/organizations/{parent.Id}/strata-lots";
        using var denied = await member.GetAsync(root, ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await organizations.AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var path = root + "/" + Guid.NewGuid(); var key = Guid.NewGuid().ToString("D");
        using var saved = await Mutate(member, HttpMethod.Patch, path, UnitRequest(0), key); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await organizations.AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var retry = await Mutate(member, HttpMethod.Patch, path, UnitRequest(0), key); Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);
        using var withdrawn = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
    }
}
