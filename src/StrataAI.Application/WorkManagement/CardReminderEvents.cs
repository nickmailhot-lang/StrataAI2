using StrataAI.Application.Common;

namespace StrataAI.Application.WorkManagement;

public interface ICardReminderEventPublisher
{
    Task PublishAsync(CardRecord card, CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct);
}

// Both personal commands and Card date/lifecycle participants publish the same
// private event contract. No shared watcher notification is produced here.
public sealed class CardReminderEvents(IWorkManagementStore work, IWorkEventStore events, IClock clock) : ICardReminderEventPublisher
{
    public async Task PublishAsync(CardRecord card, CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
    {
        if (card.Id != reminder.CardId || card.OrganizationId != reminder.OrganizationId || actorId == Guid.Empty ||
            reminder.Id == Guid.Empty || reminder.UserId == Guid.Empty || reminder.Version < 1 ||
            reminder.Status is not ("SCHEDULED" or "SUSPENDED" or "CANCELLED"))
            throw new InvalidOperationException("Invalid Reminder event scope.");
        var type = reminder.Status == "SCHEDULED" ? "REMINDER_SCHEDULED" : "REMINDER_CANCELLED";
        await work.AppendAuditAsync(card.OrganizationId, actorId, type, "Reminder", reminder.Id, correlationId, ct);
        await events.AppendAsync(new(Guid.NewGuid(), card.OrganizationId, card.BoardId, actorId,
            type, "Reminder", reminder.Id, reminder.Version, correlationId, clock.UtcNow), ct);
    }
}
