using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryWorkNotificationStore(DemoWorkTransactionScope scope) : IWorkNotificationStore, INotificationRealtimeStore, IDemoWorkTransactionParticipant
{
    public Action CaptureRollback()
    {
        lock (_notifications)
        {
            var restore = DemoRollback.Dictionary(_notifications);
            var journal = _journal.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
            return () => { lock (_notifications) {
                restore(); _journal.Clear();
                foreach (var pair in journal) _journal.Add(pair.Key, pair.Value.ToList());
            } };
        }
    }
    private readonly Dictionary<(Guid Organization, Guid Event, Guid Recipient), CardNotification> _notifications = [];
    private readonly Dictionary<(Guid Organization, Guid Recipient), List<NotificationRealtimeEvent>> _journal = [];

    public Task<long> GetRecipientSequenceAsync(Guid organizationId, Guid recipientId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (organizationId == Guid.Empty || recipientId == Guid.Empty) throw new ArgumentException("Invalid notification journal scope.");
        lock (_notifications) return Task.FromResult<long>(_journal.TryGetValue((organizationId, recipientId), out var events) ? events.Count : 0);
    }

    public Task<IReadOnlyList<NotificationRealtimeEvent>> ListRecipientEventsAsync(Guid organizationId,
        Guid recipientId, long after = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (organizationId == Guid.Empty || recipientId == Guid.Empty || after < 0)
            throw new ArgumentException("Invalid notification journal scope or cursor.");
        lock (_notifications)
            return Task.FromResult<IReadOnlyList<NotificationRealtimeEvent>>(_journal.TryGetValue((organizationId, recipientId), out var events)
                ? events.Skip((int)Math.Min(after, int.MaxValue)).Take(51).ToArray() : []);
    }

    private void Journal(CardNotification notification, bool read)
    {
        var key = (notification.OrganizationId, notification.RecipientId);
        if (!_journal.TryGetValue(key, out var events)) _journal.Add(key, events = []);
        var sequence = (long)events.Count + 1;
        events.Add(read ? NotificationRealtimeEvent.Read(notification, Guid.NewGuid(), sequence, notification.ReadAt!.Value)
            : NotificationRealtimeEvent.Created(notification, Guid.NewGuid(), sequence));
    }

    internal IReadOnlyList<CardNotification> Snapshot(Guid organization, Guid recipient)
    {
        lock (_notifications) return _notifications.Values.Where(n => n.OrganizationId == organization && n.RecipientId == recipient).ToArray();
    }

    internal IReadOnlyList<NotificationReadAcknowledgment> MarkRead(Guid organization, Guid recipient, IReadOnlyCollection<Guid> ids, DateTimeOffset now)
    {
        lock (_notifications)
        {
            var result = new List<NotificationReadAcknowledgment>();
            foreach (var entry in _notifications.Where(pair => pair.Key.Organization == organization && pair.Key.Recipient == recipient && ids.Contains(pair.Value.Id)).ToArray())
            {
                var item = entry.Value with { ReadAt = entry.Value.ReadAt ?? (now < entry.Value.CreatedAt ? entry.Value.CreatedAt : now) };
                if (entry.Value.ReadAt is null) Journal(item, true);
                _notifications[entry.Key] = item; result.Add(new(item.Id, item.ReadAt!.Value));
            }
            return result.OrderBy(n => n.Id.ToString("N"), StringComparer.Ordinal).ToArray();
        }
    }

    public Task AppendCardAssignmentAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => Append(CardNotification.From(change, recipientId));
    public Task AppendCardActivityAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => Append(CardNotification.FromActivity(change, recipientId));
    public Task AppendCardMentionAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => Append(CardNotification.FromMention(change, recipientId));
    public Task AppendCardMentionsAsync(WorkEvent change, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken = default)
        => AppendBatch(change, recipients, false, cancellationToken);
    public Task AppendCardActivitiesAsync(WorkEvent change, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken = default)
        => AppendBatch(change, recipients, true, cancellationToken);

    private Task AppendBatch(WorkEvent change, IReadOnlyList<Guid> recipients, bool activity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients); cancellationToken.ThrowIfCancellationRequested();
        if (!scope.Owns(change.OrganizationId)) throw new InvalidOperationException("Batch notifications require the originating command transaction.");
        CardNotification? Create(Guid recipient) => activity
            ? CardNotification.FromActivity(change, recipient) : CardNotification.FromMention(change, recipient);
        _ = Create(change.ActorId);
        if (recipients.Any(id => id == Guid.Empty) || recipients.Distinct().Count() != recipients.Count)
            throw new ArgumentException("Batch notification recipients must be distinct accounts.");
        var items = recipients.Select(Create).OfType<CardNotification>().ToArray();
        lock (_notifications)
        {
            // Validate the whole batch before adding rows or recipient events.
            foreach (var item in items)
                if (_notifications.TryGetValue((item.OrganizationId, item.EventId, item.RecipientId), out var existing) &&
                    existing with { Id = item.Id, ReadAt = null } != item)
                    throw new InvalidOperationException("Assignment notification identity was reused.");
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in items)
                if (_notifications.TryAdd((item.OrganizationId, item.EventId, item.RecipientId), item)) Journal(item, false);
        }
        return Task.CompletedTask;
    }

    private Task Append(CardNotification? item)
    {
        if (item is null) return Task.CompletedTask;
        lock (_notifications)
        {
            var key = (item.OrganizationId, item.EventId, item.RecipientId);
            if (_notifications.TryGetValue(key, out var existing))
            {
                if (existing with { Id = item.Id, ReadAt = null } != item)
                    throw new InvalidOperationException("Assignment notification identity was reused.");
            }
            else { _notifications.Add(key, item); Journal(item, false); }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CardNotification>> ListCardNotificationsAsync(Guid organizationId,
        Guid recipientId, Guid? after = null, CancellationToken cancellationToken = default)
    {
        lock (_notifications)
            return Task.FromResult<IReadOnlyList<CardNotification>>(_notifications.Values
                .Where(item => item.OrganizationId == organizationId && item.RecipientId == recipientId &&
                    (after is null || string.CompareOrdinal(item.Id.ToString("N"), after.Value.ToString("N")) > 0))
                .OrderBy(item => item.Id.ToString("N"), StringComparer.Ordinal).Take(51).ToArray());
    }
}
