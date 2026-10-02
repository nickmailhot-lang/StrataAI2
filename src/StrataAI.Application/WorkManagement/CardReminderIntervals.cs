namespace StrataAI.Application.WorkManagement;

public sealed record CardReminderInterval(string Code, string Label, TimeSpan BeforeDue);
public sealed record CardReminderOption(string Code, string Label, DateTimeOffset TriggerAt);

// A future delivery policy, not recipient authorization or a Worker effect.
// Due is already the canonical UTC instant, including local date-only expiry.
public static class CardReminderIntervals
{
    private static readonly CardReminderInterval[] Configured =
    [
        new("AT_DUE", "At the due time", TimeSpan.Zero),
        new("5_MINUTES", "5 minutes before", TimeSpan.FromMinutes(5)),
        new("1_HOUR", "1 hour before", TimeSpan.FromHours(1)),
        new("1_DAY", "1 day before", TimeSpan.FromDays(1)),
    ];

    public static IReadOnlyList<CardReminderOption> Available(CardRecord card, DateTimeOffset now)
    {
        if (card.LifecycleState != WorkItemLifecycleState.Active || card.DueComplete || card.DueAt is not { } due) return [];
        var result = new List<CardReminderOption>();
        foreach (var interval in Configured)
        {
            // Avoid DateTimeOffset underflow for imported historical instants.
            if (due.UtcTicks - DateTimeOffset.MinValue.UtcTicks < interval.BeforeDue.Ticks) continue;
            var trigger = due.ToUniversalTime().Subtract(interval.BeforeDue);
            if (trigger > now) result.Add(new(interval.Code, interval.Label, trigger));
        }
        return result;
    }

    public static CardReminderOption? Find(CardRecord card, string code, DateTimeOffset now) =>
        Available(card, now).SingleOrDefault(option => option.Code == code);

    public static bool IsConfigured(string code) => Configured.Any(interval => interval.Code == code);
}
