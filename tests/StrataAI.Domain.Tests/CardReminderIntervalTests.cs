using System.Globalization;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CardReminderIntervalTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T12:00:00Z", CultureInfo.InvariantCulture);
    private static CardRecord Card(DateTimeOffset? due) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Due", null, "rank", WorkItemLifecycleState.Active, Now, Now, 1) { DueAt = due, DueTimezone = due is null ? null : "UTC" };

    [Fact]
    public void Available_intervals_use_the_canonical_due_and_return_only_future_triggers()
    {
        var options = CardReminderIntervals.Available(Card(Now.AddMinutes(30)), Now);
        Assert.Equal(["AT_DUE", "5_MINUTES"], options.Select(option => option.Code));
        Assert.Equal([Now.AddMinutes(30), Now.AddMinutes(25)], options.Select(option => option.TriggerAt));
        Assert.Null(CardReminderIntervals.Find(Card(Now.AddMinutes(30)), "1_DAY", Now));
        Assert.Null(CardReminderIntervals.Find(Card(Now.AddMinutes(30)), "UNKNOWN", Now));
        Assert.Null(CardReminderIntervals.Find(Card(Now.AddMinutes(30)), "at_due", Now));
    }

    [Fact]
    public void Far_future_deadlines_offer_all_supported_intervals_and_preserve_UTC_microseconds()
    {
        var due = Now.AddDays(2).AddTicks(1234560);
        var options = CardReminderIntervals.Available(Card(due), Now);
        Assert.Equal(4, options.Count);
        Assert.Equal(due.AddDays(-1), Assert.Single(options, option => option.Code == "1_DAY").TriggerAt);
        Assert.All(options, option => Assert.Equal(TimeSpan.Zero, option.TriggerAt.Offset));
    }

    [Theory]
    [InlineData(WorkItemLifecycleState.Active, true)]
    [InlineData(WorkItemLifecycleState.Archived, false)]
    [InlineData(WorkItemLifecycleState.Deleted, false)]
    public void Completion_and_inactive_Cards_offer_no_reminder(WorkItemLifecycleState state, bool complete)
    {
        Assert.Empty(CardReminderIntervals.Available(Card(Now.AddDays(2)) with { LifecycleState = state, DueComplete = complete }, Now));
    }

    [Fact]
    public void Missing_due_expired_due_and_exact_current_trigger_are_unavailable()
    {
        Assert.Empty(CardReminderIntervals.Available(Card(null), Now));
        Assert.Empty(CardReminderIntervals.Available(Card(Now.AddTicks(-10)), Now));
        Assert.Empty(CardReminderIntervals.Available(Card(Now), Now));
        Assert.Null(CardReminderIntervals.Find(Card(Now.AddHours(1)), "1_HOUR", Now));
        Assert.Empty(CardReminderIntervals.Available(Card(DateTimeOffset.MinValue), Now));
    }

    [Fact]
    public void Date_only_DST_deadline_uses_its_actual_UTC_expiry_instead_of_wall_clock_subtraction()
    {
        var before = DateTimeOffset.Parse("2026-03-07T00:00:00Z", CultureInfo.InvariantCulture);
        Assert.True(CardDateInput.TryNormalize(new(null, "2026-03-08", "America/Vancouver", false, false, 1), out var dates));
        Assert.NotNull(dates);
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T06:59:59.999999Z", CultureInfo.InvariantCulture),
            CardReminderIntervals.Find(Card(dates.DueAt), "1_DAY", before)!.TriggerAt);
    }
}
