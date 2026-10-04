using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CardMassMentionQuotaTests
{
    private sealed class Clock : IClock
    { public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Actor : ICommandActorAuthorization
    {
        public bool Allowed = true;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(Allowed);
    }
    [Fact]
    public async Task PRD_15_DemoQuotaRollsBackLateActorRefusalAndSharesRollingActorBoardWindow()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new Clock(); var actorPolicy = new Actor();
        var services = new ServiceCollection(); var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddSingleton<IClock>(clock); services.AddSingleton<ICommandActorAuthorization>(actorPolicy);
        services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime); services.AddStrataAiOrganizations(runtime);
        services.AddStrataAiWorkManagement(runtime); using var provider = services.BuildServiceProvider();
        var quota = provider.GetRequiredService<ICardMassMentionQuota>(); var events = provider.GetRequiredService<IWorkEventStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var tenant = Guid.NewGuid(); var board = Guid.NewGuid(); var actor = Guid.NewGuid(); var card = Guid.NewGuid();
        WorkEvent Source() => new(Guid.NewGuid(), tenant, board, actor, "MENTION_CREATED", "Card", card, 1, "quota-fixture", clock.UtcNow);
        async Task<WorkOperation<bool>> Reserve(WorkEvent source, bool lateRefuse = false)
            => await unit.ExecuteReadAsync(source.OrganizationId, source.ActorId, "fixture_denied", () => Task.FromResult(true), async () =>
            {
                await events.AppendAsync(source, ct);
                if (!await quota.TryReserveAsync(source, ct)) return WorkOperation<bool>.Failure("mass_mention_rate_limited");
                actorPolicy.Allowed = !lateRefuse;
                return WorkOperation<bool>.Success(true);
            }, ct);
        var first = Source();
        await Assert.ThrowsAsync<InvalidOperationException>(() => quota.TryReserveAsync(first, ct));
        Assert.Equal("session_unavailable", (await Reserve(first, true)).ErrorCode); actorPolicy.Allowed = true;
        Assert.True((await Reserve(first)).Succeeded);
        Assert.True((await Reserve(first)).Succeeded);
        Assert.True((await Reserve(Source())).Succeeded); Assert.True((await Reserve(Source())).Succeeded);
        var fourth = Source(); Assert.Equal("mass_mention_rate_limited", (await Reserve(fourth)).ErrorCode);
        Assert.True((await Reserve(Source() with { ActorId = Guid.NewGuid() })).Succeeded);
        Assert.True((await Reserve(Source() with { BoardId = Guid.NewGuid() })).Succeeded);
        Assert.True((await Reserve(Source() with { OrganizationId = Guid.NewGuid() })).Succeeded);
        clock.UtcNow = clock.UtcNow.AddMinutes(10).AddTicks(-10);
        Assert.Equal("mass_mention_rate_limited", (await Reserve(fourth)).ErrorCode);
        clock.UtcNow = clock.UtcNow.AddTicks(10);
        Assert.True((await Reserve(fourth)).Succeeded);
        Assert.True((await Reserve(first)).Succeeded); // Original identity stays free after expiry.
        clock.UtcNow = clock.UtcNow.AddMinutes(-1); // A backward clock cannot evade future reservations.
        Assert.Equal("mass_mention_rate_limited", (await Reserve(Source())).ErrorCode);
    }
}
