using System.Collections.Concurrent;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo queues belong to this host lifetime and never access production providers.
internal sealed class InMemoryCardReminderJobPublisher : ICardReminderJobPublisher
{
    private readonly ConcurrentDictionary<(Guid Organization, string Key), NewBackgroundJob> _jobs = new();

    public Task PublishAsync(CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var job = CardReminderJobs.Create(reminder, actorId, correlationId);
        _jobs.TryAdd((job.OrganizationId, job.IdempotencyKey), job);
        return Task.CompletedTask;
    }
}
