using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("SUSPENDED", false)]
    [InlineData("SUSPENDED", true)]
    [InlineData("DEACTIVATED", false)]
    [InlineData("DEACTIVATED", true)]
    public async Task PRD_27_Demo_operational_lifecycle_HTTP_session_expiry_during_store_IO_rolls_back_all_effects(string target, bool afterWrite)
    {
        var ct = TestContext.Current.CancellationToken;
        LifecycleWriteFence? fence = null;
        var clock = new LifecycleExpiryClock();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            var original = services.Single(item => item.ServiceType == typeof(IOrganizationOperationalLifecycleStore));
            services.Remove(original);
            services.AddSingleton<IOrganizationOperationalLifecycleStore>(provider =>
                fence = new((IOrganizationOperationalLifecycleStore)original.ImplementationFactory!(provider)));
        });
        using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Session race private parent", ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var originalParent = await organizations.FindOrganizationAsync(parent.Id, ct);
        clock.UtcNow = originalParent!.UpdatedAt.AddSeconds(1);
        var audits = app.Services.GetRequiredService<InMemoryWorkManagementStore>();
        var originalAudits = audits.AuditSnapshot(parent.Id).ToArray();
        var route = $"/organizations/{parent.Id}/operational-lifecycle";
        var key = Guid.NewGuid();
        var sessionHash = app.Services.GetRequiredService<ISecureTokenService>().Hash(cookie.Split('=', 2)[1]);
        var session = await app.Services.GetRequiredService<IIdentityStore>().FindActiveSessionAsync(sessionHash, clock.UtcNow, ct);
        Assert.NotNull(session);
        _ = app.Services.GetRequiredService<IOrganizationOperationalLifecycleStore>();
        fence!.AfterWrite = afterWrite;
        fence.Withdraw = async () =>
        {
            if (afterWrite)
            {
                Assert.Equal(parent.Version + 1, (await organizations.FindOrganizationAsync(parent.Id, ct))!.Version);
                Assert.Single(audits.AuditSnapshot(parent.Id), a => a.EventType == "ORGANIZATION_" + target);
            }
            clock.UtcNow = session.ExpiresAt;
            await Task.Yield();
        };
        using var denied = await Mutate(owner, HttpMethod.Patch, route,
            new { status = target, version = parent.Version }, key.ToString("D")).WaitAsync(TimeSpan.FromSeconds(8), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain(parent.Name, await denied.Content.ReadAsStringAsync(ct));
        Assert.Equal(1, fence.Withdrawals);
        Assert.Equal(originalParent, await organizations.FindOrganizationAsync(parent.Id, ct));
        Assert.Equal(originalAudits, audits.AuditSnapshot(parent.Id).ToArray());
        using var expiredSession = await owner.GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredSession.StatusCode);

        // A trusted in-process read still has current account/Organization
        // admission. It proves the failed HTTP write left no ready event.
        var replay = await app.Services.GetRequiredService<OrganizationOperationalLifecycleReplayService>()
            .ReadAsync(parent.Id, actor, 0, ct);
        Assert.True(replay.Succeeded, replay.ErrorCode);
        Assert.Empty(replay.Value!.Events);
        Assert.Equal(parent.Version, replay.Value.Version);
        // Reuse the same key under current authority: no failed-write receipt
        // may return an old success or reserve the key without its parent change.
        var service = app.Services.GetRequiredService<OrganizationOperationalLifecycleService>();
        var changed = await service.ChangeAsync(parent.Id, actor,
            target == "SUSPENDED" ? OrganizationStatus.Suspended : OrganizationStatus.Deactivated,
            parent.Version, key, "fresh-authority-after-expiry", ct);
        Assert.True(changed.Succeeded, changed.ErrorCode);
        Assert.Equal(parent.Version + 1, changed.Value!.Version);
        Assert.Single(audits.AuditSnapshot(parent.Id), a => a.EventType == "ORGANIZATION_" + target);
    }

    private sealed class LifecycleExpiryClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class LifecycleWriteFence(IOrganizationOperationalLifecycleStore inner) : IOrganizationOperationalLifecycleStore
    {
        public bool AfterWrite { get; set; }
        public Func<Task>? Withdraw { get; set; }
        public int Withdrawals { get; private set; }
        public Task<OrganizationOperationalLifecycleScope?> ReadScopeAsync(Guid organization, Guid actor, CancellationToken ct)
            => inner.ReadScopeAsync(organization, actor, ct);
        public Task<OrganizationOperationalLifecycleReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
            => inner.ReadReceiptAsync(organization, actor, key, ct);
        public async Task<OrganizationOperationalLifecycleWriteResult> WriteAsync(long expectedVersion,
            OrganizationOperationalLifecycleTransition transition, OrganizationOperationalLifecycleReceipt receipt, CancellationToken ct)
        {
            var withdraw = Withdraw; Withdraw = null;
            if (!AfterWrite && withdraw is not null) { await withdraw(); Withdrawals++; }
            var result = await inner.WriteAsync(expectedVersion, transition, receipt, ct);
            if (AfterWrite && withdraw is not null) { await withdraw(); Withdrawals++; }
            return result;
        }
    }
}
