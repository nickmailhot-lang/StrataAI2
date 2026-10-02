using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CardReminderStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T12:00:00Z", CultureInfo.InvariantCulture);
    private static CardRecord Card() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Reminder", null, "rank", WorkItemLifecycleState.Active, Now, Now, 1) { DueAt = Now.AddDays(2), DueTimezone = "UTC" };

    [Fact]
    public async Task Personal_records_keep_identity_and_invalidate_generation_on_reschedule_suspend_cancel_and_reenable()
    {
        var ct = TestContext.Current.CancellationToken; var services = new ServiceCollection();
        services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test")); using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ICardReminderStore>(); var card = Card(); var user = Guid.NewGuid();
        var original = await store.SetAsync(card, user, "1_DAY", true, 0, Now, ct); Assert.NotNull(original);
        Assert.Equal(1, original.Version); Assert.Equal(1, original.Generation); Assert.Equal("SCHEDULED", original.Status);
        Assert.Equal(card.DueAt!.Value.AddDays(-1), original.TriggerAt); Assert.Equal(card.DueAt, original.DueAt);
        Assert.Equal(original, await store.SetAsync(card, user, "1_DAY", true, 1, Now.AddMinutes(1), ct));
        Assert.Null(await store.SetAsync(card, user, "1_HOUR", true, 0, Now, ct));
        var moved = card with { ListId = Guid.NewGuid(), BoardId = Guid.NewGuid(), Version = 2 };
        Assert.Equal(original, await store.SetAsync(moved, user, "1_DAY", true, 1, Now.AddMinutes(2), ct));
        var changed = await store.SetAsync(moved with { DueAt = card.DueAt.Value.AddDays(1) }, user, "1_DAY", true, 1, Now.AddMinutes(3), ct);
        Assert.NotNull(changed); Assert.Equal(original.Id, changed.Id); Assert.Equal(original.CreatedAt, changed.CreatedAt);
        Assert.Equal(2, changed.Version); Assert.Equal(2, changed.Generation);
        var completed = await store.SetAsync(card with { DueComplete = true }, user, "1_DAY", true, 2, Now.AddMinutes(4), ct);
        Assert.NotNull(completed); Assert.Equal("SUSPENDED", completed.Status); Assert.True(completed.Enabled); Assert.Null(completed.TriggerAt);
        var cancelled = await store.SetAsync(card, user, "1_DAY", false, 3, Now.AddMinutes(5), ct);
        Assert.NotNull(cancelled); Assert.Equal("CANCELLED", cancelled.Status); Assert.False(cancelled.Enabled);
        Assert.Null(cancelled.TriggerAt); Assert.Null(cancelled.DueAt); Assert.Empty(await store.ListEnabledForCardAsync(card.OrganizationId, card.Id, ct));
        var enabled = await store.SetAsync(card, user, "1_HOUR", true, 4, Now.AddMinutes(6), ct);
        Assert.NotNull(enabled); Assert.Equal(original.Id, enabled.Id); Assert.Equal(5, enabled.Generation); Assert.Equal(5, enabled.Version);
        Assert.Equal("SCHEDULED", enabled.Status); Assert.Equal(card.DueAt.Value.AddHours(-1), enabled.TriggerAt);
    }

    [Fact]
    public async Task Candidate_planning_keeps_all_users_without_cross_tenant_or_personal_lookup_disclosure()
    {
        var ct = TestContext.Current.CancellationToken; var services = new ServiceCollection();
        services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test")); using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ICardReminderStore>(); var card = Card();
        var users = Enumerable.Range(0, 76).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var user in users) Assert.NotNull(await store.SetAsync(card, user, "AT_DUE", true, 0, Now, ct));
        Assert.NotNull(await store.SetAsync(card with { OrganizationId = Guid.NewGuid() }, Guid.NewGuid(), "AT_DUE", true, 0, Now, ct));
        var rows = await store.ListEnabledForCardAsync(card.OrganizationId, card.Id, ct);
        Assert.Equal(76, rows.Count); Assert.Equal(76, rows.Select(row => row.UserId).Distinct().Count());
        Assert.Null(await store.FindAsync(card.OrganizationId, Guid.NewGuid(), card.Id, ct));
        Assert.Null(await store.FindAsync(Guid.NewGuid(), users[0], card.Id, ct));
        Assert.Equal(users[0], (await store.FindAsync(card.OrganizationId, users[0], card.Id, ct))!.UserId);
    }

    [Theory]
    [InlineData(WorkItemLifecycleState.Archived, false)]
    [InlineData(WorkItemLifecycleState.Deleted, false)]
    [InlineData(WorkItemLifecycleState.Active, true)]
    public void Inactive_or_completed_deadlines_suspend_the_plan_without_losing_interval_intent(WorkItemLifecycleState state, bool complete)
    {
        var plan = CardReminderPlan.For(Card() with { LifecycleState = state, DueComplete = complete }, "1_DAY", true, Now);
        Assert.Equal("SUSPENDED", plan.Status); Assert.True(plan.Enabled); Assert.Null(plan.TriggerAt);
    }

    [Fact]
    public void Clearing_or_shortening_due_suspends_and_unconfigured_intervals_cannot_enter_persistence()
    {
        Assert.Equal("SUSPENDED", CardReminderPlan.For(Card() with { DueAt = null }, "1_DAY", true, Now).Status);
        Assert.Equal("SUSPENDED", CardReminderPlan.For(Card() with { DueAt = Now.AddMinutes(5) }, "1_DAY", true, Now).Status);
        Assert.Throws<ArgumentException>(() => CardReminderPlan.For(Card(), "unknown", true, Now));
    }
}
