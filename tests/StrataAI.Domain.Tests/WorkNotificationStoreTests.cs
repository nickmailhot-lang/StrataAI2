using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class WorkNotificationStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_notifications_validate_all_recipients_before_effects_preserve_replay_and_roll_back(bool activity)
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo();
        var store = services.GetRequiredService<IWorkNotificationStore>();
        var journal = services.GetRequiredService<INotificationRealtimeStore>();
        var unit = services.GetRequiredService<IWorkManagementUnitOfWork>();
        var change = Assignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with
            { EventType = activity ? "CARD_UPDATED" : "MENTION_CREATED" };
        var recipients = Enumerable.Range(0, 76).Select(_ => Guid.NewGuid()).Append(change.ActorId).ToArray();
        Task Append(WorkEvent source, IReadOnlyList<Guid> targets, CancellationToken token) => activity
            ? store.AppendCardActivitiesAsync(source, targets, token) : store.AppendCardMentionsAsync(source, targets, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(change, recipients, ct));
        var refused = await unit.ExecuteReadAsync(change.OrganizationId, null, "denied", () => Task.FromResult(true), async () =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Append(change, [recipients[0], Guid.Empty], ct));
            await Assert.ThrowsAsync<ArgumentException>(() => Append(change, [recipients[0], recipients[0]], ct));
            await Assert.ThrowsAsync<ArgumentException>(() => Append(change with { EventType = "WATCH_CREATED" }, [], ct));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Append(change, recipients, cancelled.Token));
            Assert.Empty(await store.ListCardNotificationsAsync(change.OrganizationId, recipients[0], cancellationToken: ct));
            await Append(change, recipients, ct);
            var first = Assert.Single(await store.ListCardNotificationsAsync(change.OrganizationId, recipients[0], cancellationToken: ct));
            await Append(change, recipients.Reverse().ToArray(), ct);
            Assert.Equal(first, Assert.Single(await store.ListCardNotificationsAsync(change.OrganizationId, recipients[0], cancellationToken: ct)));
            foreach (var recipient in recipients.Take(76))
            {
                var item = Assert.Single(await store.ListCardNotificationsAsync(change.OrganizationId, recipient, cancellationToken: ct));
                Assert.Equal(change.EventType, item.NotificationType);
                Assert.Equal(item.Id, Assert.Single(await journal.ListRecipientEventsAsync(change.OrganizationId, recipient, cancellationToken: ct)).EntityId);
            }
            Assert.Empty(await journal.ListRecipientEventsAsync(change.OrganizationId, change.ActorId, cancellationToken: ct));
            var newRecipient = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Append(change with { EntityId = Guid.NewGuid() }, [newRecipient, recipients[0]], ct));
            Assert.Empty(await store.ListCardNotificationsAsync(change.OrganizationId, newRecipient, cancellationToken: ct));
            Assert.Empty(await journal.ListRecipientEventsAsync(change.OrganizationId, newRecipient, cancellationToken: ct));
            return WorkOperation<bool>.Failure("fixture_refused");
        }, ct);
        Assert.Equal("fixture_refused", refused.ErrorCode);
        foreach (var recipient in recipients)
        {
            Assert.Empty(await store.ListCardNotificationsAsync(change.OrganizationId, recipient, cancellationToken: ct));
            Assert.Empty(await journal.ListRecipientEventsAsync(change.OrganizationId, recipient, cancellationToken: ct));
        }
    }

    [Fact]
    public async Task Demo_private_journal_creation_is_deduplicated_bounded_recipient_scoped_and_rolls_back_with_notifications()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo();
        var store = services.GetRequiredService<IWorkNotificationStore>();
        var journal = services.GetRequiredService<INotificationRealtimeStore>();
        var organization = Guid.NewGuid(); var recipient = Guid.NewGuid(); var board = Guid.NewGuid(); var actor = Guid.NewGuid();
        var change = Assignment(organization, board, actor);
        await store.AppendCardAssignmentAsync(change, recipient, ct);
        var original = Assert.Single(await journal.ListRecipientEventsAsync(organization, recipient, cancellationToken: ct));
        await store.AppendCardAssignmentAsync(change, recipient, ct);
        Assert.Equal(original, Assert.Single(await journal.ListRecipientEventsAsync(organization, recipient, cancellationToken: ct)));
        Assert.Equal("NOTIFICATION_CREATED", original.EventType); Assert.Equal("1", original.Sequence);
        Assert.Equal(Assert.Single(await store.ListCardNotificationsAsync(organization, recipient, cancellationToken: ct)).Id, original.EntityId);
        await store.AppendCardAssignmentAsync(change, actor, ct);
        Assert.Empty(await journal.ListRecipientEventsAsync(organization, actor, cancellationToken: ct));
        var unit = services.GetRequiredService<IWorkManagementUnitOfWork>();
        var rolledBack = await unit.ExecuteReadAsync<int>(organization, null, "journal_denied", () => Task.FromResult(true), async () => {
        for (var index = 0; index < 51; index++) await store.AppendCardAssignmentAsync(Assignment(organization, board, actor), recipient, ct);
        var first = await journal.ListRecipientEventsAsync(organization, recipient, cancellationToken: ct);
        Assert.Equal(51, first.Count);
        var second = await journal.ListRecipientEventsAsync(organization, recipient, 50, ct); Assert.Equal(2, second.Count);
        Assert.Equal(52, first.Take(50).Concat(second).Select(e => e.EventId).Distinct().Count());
        Assert.Empty(await journal.ListRecipientEventsAsync(Guid.NewGuid(), recipient, cancellationToken: ct));
        Assert.Empty(await journal.ListRecipientEventsAsync(organization, Guid.NewGuid(), cancellationToken: ct));
        return WorkOperation<int>.Failure("journal_test_rollback");
        }, ct);
        Assert.False(rolledBack.Succeeded);
        Assert.Equal(original, Assert.Single(await journal.ListRecipientEventsAsync(organization, recipient, cancellationToken: ct)));
        Assert.Single(await store.ListCardNotificationsAsync(organization, recipient, cancellationToken: ct));
        await store.AppendCardAssignmentAsync(Assignment(organization, board, actor), recipient, ct);
        Assert.Equal("2", (await journal.ListRecipientEventsAsync(organization, recipient, cancellationToken: ct))[1].Sequence);
    }

    private static ServiceProvider Demo()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new JournalClock());
        var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        services.AddStrataAiOrganizations(runtime);
        services.AddSingleton<ICommandActorAuthorization>(new JournalActor());
        services.AddStrataAiWorkManagement(runtime);
        return services.BuildServiceProvider();
    }

    private sealed class JournalClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class JournalActor : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(true);
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
        var original = Assert.Single(await store.ListCardNotificationsAsync(organization, recipient, cancellationToken: ct));
        await store.AppendCardAssignmentAsync(change, recipient, ct);
        Assert.Equal(original, Assert.Single(await store.ListCardNotificationsAsync(organization, recipient, cancellationToken: ct)));
        await store.AppendCardAssignmentAsync(change, change.ActorId, ct);
        Assert.Empty(await store.ListCardNotificationsAsync(organization, change.ActorId, cancellationToken: ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendCardAssignmentAsync(change with { EntityId = Guid.NewGuid() }, recipient, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendCardAssignmentAsync(change with { EventType = "CARD_MEMBER_REMOVED" }, recipient, ct));
        Assert.Equal(original, Assert.Single(await store.ListCardNotificationsAsync(organization, recipient, cancellationToken: ct)));
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
        var first = await store.ListCardNotificationsAsync(organization, recipient, cancellationToken: ct);
        Assert.Equal(51, first.Count);
        var second = await store.ListCardNotificationsAsync(organization, recipient, first[49].Id, ct);
        Assert.Equal(2, second.Count);
        Assert.Equal(52, first.Take(50).Concat(second).Select(n => n.Id).Distinct().Count());
        Assert.All(first.Concat(second), n => { Assert.Equal(organization, n.OrganizationId); Assert.Equal(recipient, n.RecipientId); });
        Assert.Empty(await store.ListCardNotificationsAsync(Guid.NewGuid(), recipient, cancellationToken: ct));
        Assert.Empty(await store.ListCardNotificationsAsync(organization, Guid.NewGuid(), cancellationToken: ct));
    }

    [Theory]
    [InlineData("CARD_CREATED")]
    [InlineData("CARD_UPDATED")]
    [InlineData("CARD_MOVED")]
    [InlineData("CARD_ARCHIVED")]
    [InlineData("CARD_RESTORED")]
    [InlineData("CARD_MEMBER_ADDED")]
    [InlineData("CARD_MEMBER_REMOVED")]
    [InlineData("LABEL_ADDED")]
    [InlineData("LABEL_REMOVED")]
    public async Task Activity_notification_replay_preserves_type_and_first_identity_and_suppresses_actor(string type)
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo();
        var store = services.GetRequiredService<IWorkNotificationStore>(); var recipient = Guid.NewGuid();
        var change = Assignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with { EventType = type };
        await store.AppendCardActivityAsync(change, recipient, ct);
        var first = Assert.Single(await store.ListCardNotificationsAsync(change.OrganizationId, recipient, cancellationToken: ct));
        Assert.Equal(type, first.NotificationType);
        await store.AppendCardActivityAsync(change, recipient, ct);
        Assert.Equal(first, Assert.Single(await store.ListCardNotificationsAsync(change.OrganizationId, recipient, cancellationToken: ct)));
        await store.AppendCardActivityAsync(change, change.ActorId, ct);
        Assert.Empty(await store.ListCardNotificationsAsync(change.OrganizationId, change.ActorId, cancellationToken: ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendCardActivityAsync(change with { EntityId = Guid.NewGuid() }, recipient, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendCardActivityAsync(change with { EventType = "WATCH_CREATED" }, recipient, ct));
        Assert.Equal(first, Assert.Single(await store.ListCardNotificationsAsync(change.OrganizationId, recipient, cancellationToken: ct)));
    }
}
