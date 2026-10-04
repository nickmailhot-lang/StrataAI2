using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class CardReminderSchedulingTests
{
    [Fact]
    public async Task Date_change_republishes_every_personal_generation_and_completion_clear_archive_suspend_without_jobs()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new Clock();
        var card = Card(clock.UtcNow); var actor = Guid.NewGuid(); var users = Enumerable.Range(0, 76).Select(_ => Guid.NewGuid()).ToArray();
        var services = new ServiceCollection(); services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<ICardReminderStore>();
        foreach (var user in users) await store.SetAsync(card, user, "1_HOUR", true, 0, clock.UtcNow, ct);
        var disabled = Guid.NewGuid(); await store.SetAsync(card, disabled, "AT_DUE", false, 0, clock.UtcNow, ct);
        var publisher = new Publisher(); var events = new Events(); var scheduling = new CardReminderScheduling(store, publisher, clock, events, new Eligibility(true));
        var changed = card with { DueAt = card.DueAt!.Value.AddDays(1), Version = 2 };
        await scheduling.RescheduleAsync(card, changed, actor, "reschedule-test", ct);
        Assert.Equal(76, publisher.Rows.Count); Assert.Equal(76, publisher.Rows.Select(row => row.UserId).Distinct().Count());
        Assert.All(publisher.Rows, row => { Assert.Equal(2, row.Generation); Assert.Equal(changed.DueAt, row.DueAt); });
        Assert.All(publisher.Actors, value => Assert.Equal(actor, value));
        Assert.All(publisher.Correlations, value => Assert.Equal("reschedule-test", value));
        Assert.Equal(76, events.Rows.Count); Assert.All(events.Rows, row => Assert.Equal("SCHEDULED", row.Status)); events.Rows.Clear();
        publisher.Rows.Clear();
        var completed = changed with { DueComplete = true, Version = 3 };
        await scheduling.RescheduleAsync(changed, completed, actor, "complete", ct);
        Assert.Empty(publisher.Rows); Assert.All(await store.ListEnabledForCardAsync(card.OrganizationId, card.Id, ct),
            row => { Assert.Equal(3, row.Generation); Assert.Equal("SUSPENDED", row.Status); Assert.Null(row.TriggerAt); });
        Assert.Equal(76, events.Rows.Count); Assert.All(events.Rows, row => Assert.Equal("SUSPENDED", row.Status));
        var reopened = completed with { DueComplete = false, Version = 4 };
        await scheduling.RescheduleAsync(completed, reopened, actor, "reopen", ct);
        Assert.Equal(76, publisher.Rows.Count); Assert.All(publisher.Rows, row => Assert.Equal(4, row.Generation)); publisher.Rows.Clear();
        var cleared = reopened with { DueAt = null, Version = 5 };
        await scheduling.RescheduleAsync(reopened, cleared, actor, "clear", ct);
        Assert.Empty(publisher.Rows); Assert.All(await store.ListEnabledForCardAsync(card.OrganizationId, card.Id, ct),
            row => { Assert.Equal(5, row.Generation); Assert.Null(row.DueAt); Assert.Null(row.TriggerAt); });
        await scheduling.RescheduleAsync(cleared, reopened, actor, "restore-due", ct); publisher.Rows.Clear();
        var archived = reopened with { LifecycleState = WorkItemLifecycleState.Archived };
        await scheduling.RescheduleAsync(reopened, archived, actor, "archive", ct);
        Assert.Empty(publisher.Rows); Assert.All(await store.ListEnabledForCardAsync(card.OrganizationId, card.Id, ct), row => Assert.Equal("SUSPENDED", row.Status));
        Assert.Equal("CANCELLED", (await store.FindAsync(card.OrganizationId, disabled, card.Id, ct))!.Status);
    }

    [Fact]
    public async Task Unrelated_date_context_or_same_Board_card_movement_does_not_invalidate_pending_due_attempt_after_trigger()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new Clock(); var card = Card(clock.UtcNow); var user = Guid.NewGuid();
        var services = new ServiceCollection(); services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<ICardReminderStore>();
        var original = await store.SetAsync(card, user, "1_HOUR", true, 0, clock.UtcNow, ct); clock.UtcNow = card.DueAt!.Value;
        var publisher = new Publisher();
        var events = new Events();
        await new CardReminderScheduling(store, publisher, clock, events, new Eligibility(true)).RescheduleAsync(card,
            card with { StartAt = clock.UtcNow.AddDays(-1), DueTimezone = "America/Vancouver", DueHasTime = true,
                Title = "Changed", ListId = Guid.NewGuid(), Version = 2 }, Guid.NewGuid(), "unrelated", ct);
        Assert.Empty(publisher.Rows); Assert.Equal(original, await store.FindAsync(card.OrganizationId, user, card.Id, ct));
        Assert.Empty(events.Rows);
    }

    [Fact]
    public async Task Cross_card_or_tenant_rescheduling_is_rejected_before_mutation_or_publication()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new Clock(); var card = Card(clock.UtcNow);
        var services = new ServiceCollection(); services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<ICardReminderStore>(); var publisher = new Publisher();
        var events = new Events(); var scheduling = new CardReminderScheduling(store, publisher, clock, events, new Eligibility(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduling.RescheduleAsync(card, card with { Id = Guid.NewGuid() }, Guid.NewGuid(), "scope", ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduling.RescheduleAsync(card, card with { OrganizationId = Guid.NewGuid() }, Guid.NewGuid(), "scope", ct));
        Assert.Empty(publisher.Rows);
        Assert.Empty(events.Rows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cross_Board_move_retains_eligible_overdue_attempt_and_suspends_ineligible_owner(bool eligible)
    {
        var ct = TestContext.Current.CancellationToken; var clock = new Clock(); var card = Card(clock.UtcNow); var user = Guid.NewGuid();
        var services = new ServiceCollection(); services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<ICardReminderStore>();
        var original = await store.SetAsync(card, user, "1_HOUR", true, 0, clock.UtcNow, ct); clock.UtcNow = card.DueAt!.Value;
        var publisher = new Publisher(); var events = new Events(); var eligibility = new Eligibility(eligible);
        await new CardReminderScheduling(store, publisher, clock, events, eligibility).RescheduleAsync(card,
            card with { BoardId = Guid.NewGuid(), ListId = Guid.NewGuid(), Version = 2 }, Guid.NewGuid(), "move", ct);
        Assert.Equal(1, eligibility.Calls); Assert.Empty(publisher.Rows);
        var updated = await store.FindAsync(card.OrganizationId, user, card.Id, ct);
        Assert.NotNull(updated); Assert.Equal(original!.Id, updated.Id);
        if (eligible) { Assert.Equal(original, updated); Assert.Empty(events.Rows); }
        else
        {
            Assert.Equal("SUSPENDED", updated.Status); Assert.Null(updated.TriggerAt);
            Assert.Equal(original.Generation + 1, updated.Generation); Assert.Single(events.Rows);
        }
    }

    private sealed class Eligibility(bool eligible) : ICardReminderMoveEligibility
    {
        public int Calls { get; private set; }
        public Task<bool> CanReceiveAsync(CardRecord card, Guid userId, CancellationToken ct)
        { Calls++; return Task.FromResult(eligible); }
    }

    private static CardRecord Card(DateTimeOffset now) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Reminder", null, "rank", WorkItemLifecycleState.Active, now, now, 1) { DueAt = now.AddDays(2), DueTimezone = "UTC" };
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Publisher : ICardReminderJobPublisher
    {
        public List<CardReminder> Rows { get; } = []; public List<Guid> Actors { get; } = []; public List<string> Correlations { get; } = [];
        public Task PublishAsync(CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
        { Rows.Add(reminder); Actors.Add(actorId); Correlations.Add(correlationId); return Task.CompletedTask; }
    }
    private sealed class Events : ICardReminderEventPublisher
    {
        public List<CardReminder> Rows { get; } = [];
        public Task PublishAsync(CardRecord card, CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
        { Assert.Equal(card.Id, reminder.CardId); Assert.Equal(card.OrganizationId, reminder.OrganizationId); Rows.Add(reminder); return Task.CompletedTask; }
    }
}
