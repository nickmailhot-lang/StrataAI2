using StrataAI.Application.Common;

namespace StrataAI.Application.WorkManagement;

public interface ICardReminderJobPublisher
{
    // Publication must borrow the Reminder/Card command transaction.
    Task PublishAsync(CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct);
}

// Internal command participant. The caller owns Card scope, authorization and
// transaction; this participant rechecks destination eligibility for stored recipients.
public sealed class CardReminderScheduling(ICardReminderStore reminders, ICardReminderJobPublisher jobs, IClock clock,
    ICardReminderEventPublisher events, ICardReminderMoveEligibility eligibility)
{
    public async Task RescheduleAsync(CardRecord before, CardRecord after, Guid actorId, string correlationId, CancellationToken ct)
    {
        if (before.Id != after.Id || before.OrganizationId != after.OrganizationId || actorId == Guid.Empty)
            throw new InvalidOperationException("Invalid reminder rescheduling scope.");
        // Start/context/title/rank-only edits must not discard an already queued
        // overdue attempt just because its original trigger is now in the past.
        if (before.DueAt == after.DueAt && before.DueComplete == after.DueComplete &&
            before.LifecycleState == after.LifecycleState && before.BoardId == after.BoardId) return;
        var now = clock.UtcNow;
        foreach (var candidate in await reminders.ListEnabledForCardAsync(after.OrganizationId, after.Id, ct))
        {
            if (candidate.OrganizationId != after.OrganizationId || candidate.CardId != after.Id || !candidate.Enabled)
                throw new InvalidOperationException("Invalid reminder candidate scope.");
            var contextActive = before.BoardId == after.BoardId || await eligibility.CanReceiveAsync(after, candidate.UserId, ct);
            // An eligible recipient keeps an already queued overdue attempt when
            // only its Board context changed. Ineligible recipients must lose it.
            if (contextActive && before.DueAt == after.DueAt && before.DueComplete == after.DueComplete &&
                before.LifecycleState == after.LifecycleState) continue;
            var updated = await reminders.SetAsync(after, candidate.UserId, candidate.IntervalCode, true, candidate.Version, now, ct, contextActive);
            if (updated is null) throw new InvalidOperationException("Reminder changed during its owning Card transaction.");
            if (updated.Generation != candidate.Generation)
            {
                if (updated.Status == "SCHEDULED") await jobs.PublishAsync(updated, actorId, correlationId, ct);
                await events.PublishAsync(after, updated, actorId, correlationId, ct);
            }
        }
    }
}
