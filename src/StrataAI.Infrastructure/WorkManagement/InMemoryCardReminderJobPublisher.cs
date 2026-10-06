using System.Collections.Concurrent;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo queues belong to this host lifetime and never access production providers.
internal sealed class InMemoryCardReminderJobPublisher : ICardReminderJobPublisher, IDemoWorkTransactionParticipant
{
    private readonly ConcurrentDictionary<(Guid Organization, string Key), NewBackgroundJob> _jobs = new();

    public Action CaptureRollback()
    {
        var snapshot = _jobs.ToArray();
        return () => { _jobs.Clear(); foreach (var row in snapshot) _jobs.TryAdd(row.Key, row.Value); };
    }

    public Task PublishAsync(CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var job = CardReminderJobs.Create(reminder, actorId, correlationId);
        _jobs.TryAdd((job.OrganizationId, job.IdempotencyKey), job);
        return Task.CompletedTask;
    }
}
