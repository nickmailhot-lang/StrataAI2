using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryWorkEventStore(IWorkManagementStore work) : IWorkEventStore, IWorkEventReader
{
    private readonly Dictionary<(Guid Organization, Guid Id), (long Sequence, WorkEvent Event)> _events = [];
    private readonly Dictionary<(Guid Organization, Guid Board), long> _streams = [];
    public Task AppendAsync(WorkEvent change, CancellationToken cancellationToken = default)
    {
        lock (_events)
        {
            var key = (change.OrganizationId, change.EventId);
            if (_events.TryGetValue(key, out var existing))
            {
                if (existing.Event != change) throw new InvalidOperationException("Work event identity was reused.");
                return Task.CompletedTask;
            }
            var stream = (change.OrganizationId, change.BoardId);
            var sequence = _streams.GetValueOrDefault(stream) + 1;
            _streams[stream] = sequence;
            _events[key] = (sequence, change);
        }
        return Task.CompletedTask;
    }

    public async Task<WorkEventReadPage> ReadAsync(Guid organizationId, Guid boardId, long since, int limit,
        CancellationToken cancellationToken = default)
    {
        long published;
        (long Sequence, WorkEvent Event)[] window;
        lock (_events)
        {
            published = _streams.GetValueOrDefault((organizationId, boardId));
            window = _events.Values.Where(row => row.Event.OrganizationId == organizationId &&
                row.Event.BoardId == boardId && row.Sequence > since && row.Sequence <= published)
                .OrderBy(row => row.Sequence).Take(limit + 1).ToArray();
        }
        var rows = new List<WorkEventReadCandidate>();
        foreach (var row in window)
            rows.Add(new(row.Sequence, row.Event, true, await VisibleAsync(row.Event, cancellationToken)));
        return WorkEventReadWindow.Build(since, published, limit, rows);
    }

    private async Task<bool> VisibleAsync(WorkEvent change, CancellationToken cancellationToken)
    {
        if (change.EntityType == "Board") return change.EntityId == change.BoardId;
        if (change.EntityType == "Label")
        {
            var label = await work.FindLabelAsync(change.EntityId, cancellationToken);
            return label is { Deleted: false } && label.OrganizationId == change.OrganizationId && label.BoardId == change.BoardId;
        }
        if (change.EntityType == "List")
        {
            var list = await work.FindListAsync(change.EntityId, cancellationToken);
            return list is { LifecycleState: WorkItemLifecycleState.Active } &&
                list.OrganizationId == change.OrganizationId && list.BoardId == change.BoardId;
        }
        if (change.EntityType != "Card") return false;
        var card = await work.FindCardAsync(change.EntityId, cancellationToken);
        if (card is not { LifecycleState: WorkItemLifecycleState.Active } ||
            card.OrganizationId != change.OrganizationId || card.BoardId != change.BoardId) return false;
        var parent = await work.FindListAsync(card.ListId, cancellationToken);
        return parent is { LifecycleState: WorkItemLifecycleState.Active } &&
            parent.OrganizationId == change.OrganizationId && parent.BoardId == change.BoardId;
    }
}
