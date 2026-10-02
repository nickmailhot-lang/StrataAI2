using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryCardReminderStore : ICardReminderStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid Organization, Guid User, Guid Card), CardReminder> _rows = [];

    public Task<CardReminder?> FindAsync(Guid organizationId, Guid userId, Guid cardId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult(_rows.GetValueOrDefault((organizationId, userId, cardId)));
    }
    public Task<IReadOnlyList<CardReminder>> ListEnabledForCardAsync(Guid organizationId, Guid cardId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_gate)
            return Task.FromResult<IReadOnlyList<CardReminder>>(_rows.Values.Where(row => row.OrganizationId == organizationId && row.CardId == cardId && row.Enabled)
                .OrderBy(row => row.UserId.ToString("N"), StringComparer.Ordinal).ToArray());
    }
    public Task<CardReminder?> SetAsync(CardRecord card, Guid userId, string intervalCode, bool enabled,
        long expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var plan = CardReminderPlan.For(card, intervalCode, enabled, now);
        lock (_gate)
        {
            var key = (card.OrganizationId, userId, card.Id); var old = _rows.GetValueOrDefault(key);
            if ((old?.Version ?? 0) != expectedVersion) return Task.FromResult<CardReminder?>(null);
            if (old is not null && old.IntervalCode == intervalCode && plan.Matches(old)) return Task.FromResult<CardReminder?>(old);
            var row = old is null ? new CardReminder(Guid.NewGuid(), card.OrganizationId, userId, card.Id, intervalCode,
                plan.Enabled, plan.DueAt, plan.TriggerAt, plan.Status, 1, now, now, 1) : old with
                { IntervalCode = intervalCode, Enabled = plan.Enabled, DueAt = plan.DueAt, TriggerAt = plan.TriggerAt,
                    Status = plan.Status, Generation = old.Generation + 1, Version = old.Version + 1,
                    UpdatedAt = now > old.UpdatedAt ? now : old.UpdatedAt };
            _rows[key] = row; return Task.FromResult<CardReminder?>(row);
        }
    }
}
