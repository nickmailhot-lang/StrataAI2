namespace StrataAI.Application.WorkManagement;

public sealed record CardReminder(Guid Id, Guid OrganizationId, Guid UserId, Guid CardId,
    string IntervalCode, bool Enabled, DateTimeOffset? DueAt, DateTimeOffset? TriggerAt, string Status,
    long Generation, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record CardReminderPlan(bool Enabled, DateTimeOffset? DueAt, DateTimeOffset? TriggerAt, string Status)
{
    public static CardReminderPlan For(CardRecord card, string intervalCode, bool enabled, DateTimeOffset now, bool contextActive = true)
    {
        if (!CardReminderIntervals.IsConfigured(intervalCode)) throw new ArgumentException("Unsupported reminder interval.", nameof(intervalCode));
        if (!enabled) return new(false, null, null, "CANCELLED");
        var available = contextActive ? CardReminderIntervals.Find(card, intervalCode, now) : null;
        return available is null ? new(true, card.DueAt?.ToUniversalTime(), null, "SUSPENDED")
            : new(true, card.DueAt!.Value.ToUniversalTime(), available.TriggerAt, "SCHEDULED");
    }

    public bool Matches(CardReminder reminder) => Enabled == reminder.Enabled && DueAt == reminder.DueAt &&
        TriggerAt == reminder.TriggerAt && Status == reminder.Status;
}

// Internal persistence only. Callers must hold the current Card command scope
// and separately authorize recipient eligibility and explicit interval choice.
// Nothing in this store publishes a job or grants the Worker delivery authority.
public interface ICardReminderStore
{
    Task<CardReminder?> FindAsync(Guid organizationId, Guid userId, Guid cardId, CancellationToken ct);
    Task<IReadOnlyList<CardReminder>> ListEnabledForCardAsync(Guid organizationId, Guid cardId, CancellationToken ct);
    Task<CardReminder?> SetAsync(CardRecord card, Guid userId, string intervalCode, bool enabled,
        long expectedVersion, DateTimeOffset now, CancellationToken ct, bool contextActive = true);
}
