using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class WorkNotificationStoreTests
{
    private static ServiceProvider Demo()
    {
        var services = new ServiceCollection();
        services.AddStrataAiWorkManagement(new RuntimeDescriptor(RuntimeMode.Demo, "test", "test"));
        return services.BuildServiceProvider();
    }

    private static WorkEvent Assignment(Guid organization, Guid board, Guid actor) =>
        new(Guid.NewGuid(), organization, board, actor, "CARD_MEMBER_ADDED", "Card",
            Guid.NewGuid(), 2, "notification-test", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Event_recipient_replay_retains_first_notification_self_actions_are_suppressed_and_reuse_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = Demo(); var store = services.GetRequiredService<IWorkNotificationStore>();
        var organization = Guid.NewGuid(); var recipient = Guid.NewGuid();
        var change = Assignment(organization, Guid.NewGuid(), Guid.NewGuid());
        await store.AppendCardAssignmentAsync(change, recipient, ct);
        var original = Assert.Single(await store.ListCardAssignmentsAsync(organization, recipient, cancellationToken: ct));
        await store.AppendCardAssignmentAsync(change, recipient, ct);
        Assert.Equal(original, Assert.Single(await store.ListCardAssignmentsAsync(organization, recipient, cancellationToken: ct)));
        await store.AppendCardAssignmentAsync(change, change.ActorId, ct);
        Assert.Empty(await store.ListCardAssignmentsAsync(organization, change.ActorId, cancellationToken: ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendCardAssignmentAsync(change with { EntityId = Guid.NewGuid() }, recipient, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendCardAssignmentAsync(change with { EventType = "CARD_MEMBER_REMOVED" }, recipient, ct));
        Assert.Equal(original, Assert.Single(await store.ListCardAssignmentsAsync(organization, recipient, cancellationToken: ct)));
    }

    [Fact]
    public async Task Internal_storage_windows_are_bounded_seeking_unique_and_recipient_and_Organization_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = Demo(); var store = services.GetRequiredService<IWorkNotificationStore>();
        var organization = Guid.NewGuid(); var board = Guid.NewGuid(); var actor = Guid.NewGuid(); var recipient = Guid.NewGuid();
        for (var index = 0; index < 52; index++) await store.AppendCardAssignmentAsync(Assignment(organization, board, actor), recipient, ct);
        await store.AppendCardAssignmentAsync(Assignment(Guid.NewGuid(), board, actor), recipient, ct);
        await store.AppendCardAssignmentAsync(Assignment(organization, board, actor), Guid.NewGuid(), ct);
        var first = await store.ListCardAssignmentsAsync(organization, recipient, cancellationToken: ct);
        Assert.Equal(51, first.Count);
        var second = await store.ListCardAssignmentsAsync(organization, recipient, first[49].Id, ct);
        Assert.Equal(2, second.Count);
        Assert.Equal(52, first.Take(50).Concat(second).Select(n => n.Id).Distinct().Count());
        Assert.All(first.Concat(second), n => { Assert.Equal(organization, n.OrganizationId); Assert.Equal(recipient, n.RecipientId); });
        Assert.Empty(await store.ListCardAssignmentsAsync(Guid.NewGuid(), recipient, cancellationToken: ct));
        Assert.Empty(await store.ListCardAssignmentsAsync(organization, Guid.NewGuid(), cancellationToken: ct));
    }
}
