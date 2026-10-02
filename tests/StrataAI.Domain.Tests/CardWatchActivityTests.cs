using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CardWatchActivityTests
{
    private static CardRecord Card() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Card", null, "rank", WorkItemLifecycleState.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 3);
    private static WorkEvent Event(CardRecord card, string type) => new(Guid.NewGuid(), card.OrganizationId, card.BoardId,
        Guid.NewGuid(), type, "Card", card.Id, card.Version, "watch-activity", card.UpdatedAt);

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
    public void Configured_activity_captures_the_triggering_post_mutation_revision(string type)
    {
        var card = Card(); if (type == "CARD_ARCHIVED") card = card with { LifecycleState = WorkItemLifecycleState.Archived };
        var change = Event(card, type);
        Assert.Equal(new(card.OrganizationId, card.BoardId, card.ListId, card.Id), CardWatchActivity.Capture(change, card));
        Assert.Throws<ArgumentException>(() => CardWatchActivity.Capture(change, card with { Version = 4 }));
        Assert.Throws<ArgumentException>(() => CardWatchActivity.Capture(change, card with { OrganizationId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => CardWatchActivity.Capture(change, card with { BoardId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => CardWatchActivity.Capture(change, card with { Id = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => CardWatchActivity.Capture(change, card with { LifecycleState = WorkItemLifecycleState.Deleted }));
    }

    [Theory]
    [InlineData("WATCH_CREATED", "WatchSubscription")]
    [InlineData("WATCH_REMOVED", "WatchSubscription")]
    [InlineData("CARD_DELETED", "Card")]
    [InlineData("FUTURE_ACTIVITY", "Card")]
    [InlineData("LABEL_UPDATED", "Label")]
    [InlineData("BOARD_UPDATED", "Board")]
    [InlineData("LIST_MOVED", "List")]
    [InlineData("CARD_MOVED", "Board")]
    public void Unconfigured_or_other_entity_events_never_recursively_fan_out(string type, string entityType)
    {
        var card = Card(); var change = Event(card, type) with { EntityType = entityType };
        Assert.False(CardWatchActivity.IsRelevant(change)); Assert.Null(CardWatchActivity.Capture(change, card));
    }

    [Fact]
    public async Task Movement_uses_destination_List_and_direct_Card_watches_survive_with_no_recipient_truncation()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection(); services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IWatchSubscriptionStore>();
        var card = Card(); var source = card.ListId; var destination = Guid.NewGuid();
        var direct = Guid.NewGuid(); var sourceWatcher = Guid.NewGuid(); var destinationWatcher = Guid.NewGuid(); var boardWatcher = Guid.NewGuid();
        async Task Watch(Guid org, Guid user, string type, Guid entity) =>
            _ = await store.SetAsync(org, user, type, entity, true, 0, card.UpdatedAt, ct);
        await Watch(card.OrganizationId, direct, "CARD", card.Id);
        await Watch(card.OrganizationId, direct, "LIST", source);
        await Watch(card.OrganizationId, direct, "BOARD", card.BoardId);
        await Watch(card.OrganizationId, sourceWatcher, "LIST", source);
        await Watch(card.OrganizationId, destinationWatcher, "LIST", destination);
        await Watch(card.OrganizationId, boardWatcher, "BOARD", card.BoardId);
        var extra = Enumerable.Range(0, 75).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var user in extra) await Watch(card.OrganizationId, user, "BOARD", card.BoardId);
        await Watch(Guid.NewGuid(), Guid.NewGuid(), "CARD", card.Id);
        await Watch(card.OrganizationId, Guid.NewGuid(), "LIST", Guid.NewGuid());
        var before = await store.ListActivityCandidatesAsync(CardWatchActivity.Capture(Event(card, "CARD_CREATED"), card)!, ct);
        Assert.Equal(78, before.Count); Assert.Contains(sourceWatcher, before); Assert.DoesNotContain(destinationWatcher, before);
        var moved = card with { ListId = destination, Version = 4 };
        var after = await store.ListActivityCandidatesAsync(CardWatchActivity.Capture(Event(moved, "CARD_MOVED"), moved)!, ct);
        Assert.Equal(78, after.Count); Assert.Equal(after.Count, after.Distinct().Count());
        Assert.Contains(direct, after); Assert.Contains(boardWatcher, after); Assert.Contains(destinationWatcher, after);
        Assert.DoesNotContain(sourceWatcher, after); Assert.All(extra, user => Assert.Contains(user, after));
        await store.SetAsync(card.OrganizationId, direct, "CARD", card.Id, false, 1, card.UpdatedAt, ct);
        await store.SetAsync(card.OrganizationId, direct, "BOARD", card.BoardId, false, 1, card.UpdatedAt, ct);
        Assert.DoesNotContain(direct, await store.ListActivityCandidatesAsync(CardWatchActivity.Capture(Event(moved, "CARD_UPDATED"), moved)!, ct));
    }
}
