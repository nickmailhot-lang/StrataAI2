using StrataAI.Application.WorkManagement;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Onboarding;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkEventStore(IWorkManagementStore work, IIdentityStore identities,
    IOrganizationStore organizations, DemoWorkTransactionScope scope, InMemoryWatchSubscriptionStore watches,
    InMemoryCardReminderStore reminders, Func<IDemoBoardAuthorityProjection>? authority = null) : IWorkEventStore, IWorkEventReader, IOrganizationBoardEventReader, IActivityEventSourceStore, IActivityPrivateTargetStore, IDemoWorkTransactionParticipant
{
    internal IReadOnlyList<ActivityEventSource> ActivitySources(Guid organizationId)
    {
        if (!scope.Owns(organizationId)) throw new InvalidOperationException("Activity candidates require the owning Work transaction.");
        lock (_events) return Array.AsReadOnly(_activity.Values.Where(row => row.OrganizationId == organizationId).ToArray());
    }

    public Task<ActivityPrivateTarget?> FindAsync(Guid organizationId, Guid eventId, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || eventId == Guid.Empty || !scope.Owns(organizationId))
            throw new InvalidOperationException("Activity targets require the owning Work transaction.");
        ct.ThrowIfCancellationRequested(); WorkEvent? source;
        lock (_events) source = _events.GetValueOrDefault((organizationId, eventId)).Event;
        return Task.FromResult(source?.EntityType switch
        {
            "WatchSubscription" => watches.FindActivityTarget(organizationId, source.EntityId),
            "Reminder" => reminders.FindActivityTarget(organizationId, source.EntityId),
            _ => null,
        });
    }

    public Action CaptureRollback()
    {
        lock (_events)
        {
            var events = DemoRollback.Dictionary(_events); var streams = DemoRollback.Dictionary(_streams);
            var activity = DemoRollback.Dictionary(_activity);
            var organizationEvents = DemoRollback.Dictionary(_organizationEvents);
            var organizationStreams = DemoRollback.Dictionary(_organizationStreams);
            return () => { lock (_events) { events(); streams(); activity(); organizationEvents(); organizationStreams(); } };
        }
    }
    private readonly Dictionary<(Guid Organization, Guid Id), (long Sequence, WorkEvent Event)> _events = [];
    private readonly Dictionary<(Guid Organization, Guid Board), long> _streams = [];
    private readonly Dictionary<(Guid Organization, Guid Id), ActivityEventSource> _activity = [];
    private readonly Dictionary<(Guid Organization, Guid Id), OrganizationBoardEventCandidate> _organizationEvents = [];
    private readonly Dictionary<Guid, long> _organizationStreams = [];
    internal bool ContainsExact(WorkEvent source)
    { lock (_events) return _events.TryGetValue((source.OrganizationId, source.EventId), out var row) && row.Event == source; }
    public async Task AppendAsync(WorkEvent change, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var member = await organizations.FindMembershipAsync(change.OrganizationId, change.ActorId, cancellationToken);
        var user = member is null ? null : await identities.FindUserByIdAsync(change.ActorId, cancellationToken);
        var caption = user?.DisplayName;
        if (caption is null || caption.EnumerateRunes().Count() is < 1 or > 160 || caption.Any(char.IsControl))
            caption = $"Member {change.ActorId:D}";
        lock (_events)
        {
            var key = (change.OrganizationId, change.EventId);
            if (_events.TryGetValue(key, out var existing))
            {
                if (existing.Event != change) throw new InvalidOperationException("Work event identity was reused.");
                return;
            }
            var stream = (change.OrganizationId, change.BoardId);
            var sequence = _streams.GetValueOrDefault(stream) + 1;
            _streams[stream] = sequence;
            _events[key] = (sequence, change);
            _activity[key] = new(change.EventId, change.OrganizationId, change.BoardId, change.ActorId, caption,
                change.EventType, change.EntityType, change.EntityId, change.Version,
                new DateTimeOffset(change.CreatedAt.UtcTicks / 10 * 10, TimeSpan.Zero));
            if (change.EntityType == "Board" && change.EntityId == change.BoardId && change.EventType is
                "BOARD_CREATED" or "BOARD_UPDATED" or "BOARD_COPIED" or "BOARD_ARCHIVED" or "BOARD_RESTORED" or "BOARD_DELETED")
            {
                var organizationSequence = checked(_organizationStreams.GetValueOrDefault(change.OrganizationId) + 1);
                _organizationStreams[change.OrganizationId] = organizationSequence;
                // Demo delivery is immediate, as in its existing Board reader;
                // production readiness remains the separate Worker's source.
                _organizationEvents[key] = new(organizationSequence, change.EventId, change.BoardId,
                    change.EventType, change.Version, change.CreatedAt, true);
            }
        }
        // Raw legacy fixtures outside commands acquire no authority history.
        if (scope.Owns(change.OrganizationId) && DemoBoardAuthorityProof.Supports(change) && authority is not null)
            await authority().AppendAsync(change, cancellationToken);
    }

    public Task<IReadOnlyList<ActivityEventSource>> ReadBoardWindowAsync(Guid organizationId, Guid boardId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeEventId, CancellationToken ct = default)
        => ReadWindowAsync(organizationId, boardId, null, beforeCreatedAt, beforeEventId, ct);

    public Task<IReadOnlyList<ActivityEventSource>> ReadCardWindowAsync(Guid organizationId, Guid sourceBoardId, Guid cardId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeEventId, CancellationToken ct = default)
    {
        if (cardId == Guid.Empty) throw new ArgumentException("Activity Card identity is required.");
        return ReadWindowAsync(organizationId, sourceBoardId, cardId, beforeCreatedAt, beforeEventId, ct);
    }

    private Task<IReadOnlyList<ActivityEventSource>> ReadWindowAsync(Guid organizationId, Guid boardId, Guid? cardId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeEventId, CancellationToken ct)
    {
        if (organizationId == Guid.Empty || boardId == Guid.Empty || !scope.Owns(organizationId))
            throw new InvalidOperationException("Activity sources require the owning Work transaction.");
        ActivityEventSourceWindow.RequireCursor(beforeCreatedAt, beforeEventId); ct.ThrowIfCancellationRequested();
        lock (_events)
        {
            var rows = _activity.Values.Where(row => row.OrganizationId == organizationId && row.BoardId == boardId
                && (!cardId.HasValue || row.EntityType == "Card" && row.EntityId == cardId.Value)
                && (beforeCreatedAt is null || row.CreatedAt < beforeCreatedAt || row.CreatedAt == beforeCreatedAt
                    && string.CompareOrdinal(row.EventId.ToString("N"), beforeEventId!.Value.ToString("N")) < 0))
                .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.EventId.ToString("N"), StringComparer.Ordinal).Take(ActivityEventSourceWindow.MaximumRows).ToArray();
            return Task.FromResult<IReadOnlyList<ActivityEventSource>>(Array.AsReadOnly(rows));
        }
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
