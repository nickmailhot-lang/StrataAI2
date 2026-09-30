using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryWorkEventStore : IWorkEventStore
{
    private readonly Dictionary<(Guid Organization, Guid Id), WorkEvent> _events = [];
    public Task AppendAsync(WorkEvent change, CancellationToken cancellationToken = default)
    {
        lock (_events)
        {
            var key = (change.OrganizationId, change.EventId);
            if (_events.TryGetValue(key, out var existing) && existing != change)
                throw new InvalidOperationException("Work event identity was reused.");
            _events[key] = change;
        }
        return Task.CompletedTask;
    }
}
