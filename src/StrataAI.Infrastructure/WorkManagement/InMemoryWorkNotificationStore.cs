using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryWorkNotificationStore : IWorkNotificationStore
{
    private readonly Dictionary<(Guid Organization, Guid Event, Guid Recipient), CardNotification> _notifications = [];

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
                _notifications[entry.Key] = item; result.Add(new(item.Id, item.ReadAt!.Value));
            }
            return result.OrderBy(n => n.Id.ToString("N"), StringComparer.Ordinal).ToArray();
        }
    }

    public Task AppendCardAssignmentAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => Append(CardNotification.From(change, recipientId));
    public Task AppendCardActivityAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
        => Append(CardNotification.FromActivity(change, recipientId));

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
            else _notifications.Add(key, item);
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
