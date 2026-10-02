using System.Text.Json;
using System.Globalization;
using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.WorkManagement;

// Queue metadata is a reference, never a recipient, permission or due-time claim.
public sealed record CardReminderAttempt(Guid ReminderId, long Generation)
{
    public static CardReminderAttempt Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("reminderId", out var idValue) || idValue.ValueKind != JsonValueKind.String ||
            !idValue.TryGetGuid(out var id) || id == Guid.Empty ||
            !root.TryGetProperty("generation", out var generationValue) || generationValue.ValueKind != JsonValueKind.Number ||
            !generationValue.TryGetInt64(out var generation) || generation < 1)
            throw new InvalidOperationException("Invalid reminder references.");
        return new(id, generation);
    }
}

public enum CardReminderDeliveryResult { Delivered, Superseded, LeaseLost }

public interface ICardReminderDeliveryStore
{
    // An owning transaction must lock and revalidate the exact live lease,
    // canonical generation/due/completion/lifecycle and current recipient access.
    // Notification, private event and FIRED transition commit atomically.
    // Superseded also covers current recipient/parent access loss: no effect.
    Task<CardReminderDeliveryResult> DeliverAsync(ClaimedBackgroundJob job, CardReminderAttempt attempt,
        CancellationToken ct);
}

public sealed class CardReminderDeliveryHandler(ICardReminderDeliveryStore delivery) : IBackgroundJobHandler
{
    public const string Type = "CARD_REMINDER";
    public const string Service = "card-reminder-delivery";
    public string JobType => Type;
    public string ServiceIdentity => Service;

    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        if (job.JobType != Type || job.ServiceIdentity != Service || job.Id == Guid.Empty ||
            job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty)
            throw new InvalidOperationException("Invalid reminder job scope.");
        var attempt = CardReminderAttempt.Parse(job.SafeMetadataJson);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await delivery.DeliverAsync(job, attempt, cancellationToken);
        if (result is not (CardReminderDeliveryResult.Delivered or CardReminderDeliveryResult.Superseded))
            throw new InvalidOperationException("Reminder lease is unavailable.");
    }
}

public static class CardReminderJobs
{
    public static NewBackgroundJob Create(CardReminder reminder, Guid actorId, string correlationId)
    {
        if (reminder.Id == Guid.Empty || reminder.OrganizationId == Guid.Empty || reminder.UserId == Guid.Empty ||
            reminder.CardId == Guid.Empty || actorId == Guid.Empty || reminder.Generation < 1 || reminder.Version < reminder.Generation ||
            !reminder.Enabled || reminder.Status != "SCHEDULED" || reminder.DueAt is null || reminder.TriggerAt is null ||
            !CardReminderIntervals.IsConfigured(reminder.IntervalCode) ||
            reminder.DueAt.Value.UtcTicks - reminder.TriggerAt.Value.UtcTicks != CardReminderIntervals.Duration(reminder.IntervalCode).Ticks)
            throw new InvalidOperationException("Invalid scheduled reminder.");
        return new(Guid.NewGuid(), reminder.OrganizationId, CardReminderDeliveryHandler.Type,
            $"card-reminder/{reminder.Id:N}/{reminder.Generation.ToString(CultureInfo.InvariantCulture)}", actorId, CardReminderDeliveryHandler.Service,
            correlationId, JsonSerializer.Serialize(new { reminderId = reminder.Id, generation = reminder.Generation }))
            { AvailableAt = reminder.TriggerAt.Value.ToUniversalTime() };
    }
}
