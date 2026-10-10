using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo queues belong to this host lifetime and never access production providers.
internal sealed class InMemoryCardReminderJobPublisher(InMemoryBackgroundJobStore jobs) : ICardReminderJobPublisher
{
    public Task PublishAsync(CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var job = CardReminderJobs.Create(reminder, actorId, correlationId);
        jobs.Publish(job, ct);
        return Task.CompletedTask;
    }
}
