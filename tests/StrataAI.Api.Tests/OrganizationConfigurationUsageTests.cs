using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_configuration_usage_HTTP_counts_only_commits_and_excludes_private_content()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Private usage Organization", ct);
        var path = $"/organizations/{parent.Id}/configuration";
        async Task Check(long count)
        {
            using var response = await owner.GetAsync(path + "/usage", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.Private); Assert.True(response.Headers.CacheControl.NoStore);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(new[] { "adopted", "committedChangeCount", "organizationId" }, payload.EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.Equal(parent.Id, payload.GetProperty("organizationId").GetGuid());
            Assert.Equal(count > 0, payload.GetProperty("adopted").GetBoolean());
            Assert.Equal(count, payload.GetProperty("committedChangeCount").GetInt64());
            Assert.DoesNotContain("Private", await response.Content.ReadAsStringAsync(ct));
        }
        await Check(0);
        var data = new OrganizationConfigurationData("Private usage legal name", "CA-BC", "UTC",
            ManagementCompanyName: "Private usage manager");
        var key = Guid.NewGuid().ToString("D");
        using var first = await Mutate(owner, HttpMethod.Patch, path, new { version = 0, configuration = data }, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); await Check(1);
        using var second = await Mutate(owner, HttpMethod.Patch, path, new { version = 1, configuration = data with { Timezone = "America/Vancouver" } }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); await Check(2);
        using var retry = await Mutate(owner, HttpMethod.Patch, path, new { version = 0, configuration = data }, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, (await retry.Content.ReadFromJsonAsync<OrganizationConfigurationRecord>(ct))!.Version); await Check(2);
        using var stale = await Mutate(owner, HttpMethod.Patch, path, new { version = 0, configuration = data }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, path, new { version = 2, configuration = data with { LotCount = 0 } }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); await Check(2); await Check(2);
        var history = await owner.GetFromJsonAsync<OrganizationConfigurationHistoryPage>(path + "/history", ct);
        Assert.Equal(new long[] { 2, 1 }, history!.Items.Select(row => row.Version));
    }

    [Fact]
    public async Task PRD_27_configuration_usage_HTTP_requires_current_configuration_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var (actor, parent) = await ConfigurationParent(owner, "Private usage authority", ct);
        var (outsider, _) = await ConfigurationParent(other, "Other private usage authority", ct);
        var path = $"/organizations/{parent.Id}/configuration/usage";
        using var unauthenticated = await anonymous.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var foreign = await other.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.DoesNotContain("committedChangeCount", await foreign.Content.ReadAsStringAsync(ct));
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        await ConfigurationOwned(unit, parent.Id, actor, outsider, false, async () =>
        {
            await store.AddOrRestoreMemberAsync(parent.Id, outsider, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        using var admitted = await other.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        await ConfigurationOwned(unit, parent.Id, actor, outsider, false, async () =>
        {
            await store.AddOrRestoreMemberAsync(parent.Id, outsider, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        using var demoted = await other.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, demoted.StatusCode);
        Assert.DoesNotContain("committedChangeCount", await demoted.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/me", ct)).StatusCode);
    }

    private sealed class UsageAfterRead(IOrganizationConfigurationStore inner, Func<OrganizationConfigurationRecord?, OrganizationConfigurationRecord?> after)
        : IOrganizationConfigurationStore
    {
        public async Task<OrganizationConfigurationRecord?> ReadAsync(Guid organization, CancellationToken ct) => after(await inner.ReadAsync(organization, ct));
        public Task<bool> IntakeAvailableAsync(Guid organization, Guid? board, Guid? list, CancellationToken ct) => inner.IntakeAvailableAsync(organization, board, list, ct);
        public Task<OrganizationConfigurationReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptAsync(organization, actor, key, ct);
        public Task<IReadOnlyList<OrganizationConfigurationRecord>> ReadHistoryAsync(Guid organization, long? before, CancellationToken ct) => inner.ReadHistoryAsync(organization, before, ct);
        public Task<IReadOnlyList<OrganizationConfigurationEvent>> ReadEventsAsync(Guid organization, long afterVersion, CancellationToken ct) => inner.ReadEventsAsync(organization, afterVersion, ct);
        public Task<OrganizationConfigurationWriteResult> WriteAsync(long version, OrganizationConfigurationRecord record, OrganizationConfigurationReceipt receipt, CancellationToken ct) => inner.WriteAsync(version, record, receipt, ct);
    }

    [Fact]
    public async Task PRD_27_configuration_usage_withdraws_snapshot_after_account_admission_changes_during_IO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Final usage authority", ct);
        var fence = new ConfigurationFinalFence(app.Services.GetRequiredService<ICommandActorAuthorization>());
        var store = new UsageAfterRead(app.Services.GetRequiredService<IOrganizationConfigurationStore>(), record => { fence.Denied = true; return record; });
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services, store, fence);
        var result = await service.ReadUsageAsync(parent.Id, actor, ct);
        Assert.False(result.Succeeded); Assert.Equal("organization_not_found", result.ErrorCode); Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_27_configuration_usage_refuses_invalid_or_cross_Organization_sources(bool foreign)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Bound usage source", ct);
        var invalid = ConfigurationRevision(parent, actor, foreign ? 1 : 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new OrganizationConfigurationData("Private invalid source", "CA-BC", "UTC"));
        if (foreign) invalid = invalid with { OrganizationId = Guid.NewGuid() };
        var store = new UsageAfterRead(app.Services.GetRequiredService<IOrganizationConfigurationStore>(), _ => invalid);
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(app.Services, store);
        var result = await service.ReadUsageAsync(parent.Id, actor, ct);
        Assert.False(result.Succeeded); Assert.Equal("configuration_source_unavailable", result.ErrorCode); Assert.Null(result.Value);
    }
}
