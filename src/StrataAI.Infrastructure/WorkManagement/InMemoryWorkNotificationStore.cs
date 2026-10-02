using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryWorkNotificationStore : IWorkNotificationStore
{
    private readonly Dictionary<(Guid Organization, Guid Event, Guid Recipient), CardAssignmentNotification> _notifications = [];

    public Task AppendCardAssignmentAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default)
    {
        var item = CardAssignmentNotification.From(change, recipientId);
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

    public Task<IReadOnlyList<CardAssignmentNotification>> ListCardAssignmentsAsync(Guid organizationId,
        Guid recipientId, Guid? after = null, CancellationToken cancellationToken = default)
    {
        lock (_notifications)
            return Task.FromResult<IReadOnlyList<CardAssignmentNotification>>(_notifications.Values
                .Where(item => item.OrganizationId == organizationId && item.RecipientId == recipientId &&
                    (after is null || string.CompareOrdinal(item.Id.ToString("N"), after.Value.ToString("N")) > 0))
                .OrderBy(item => item.Id.ToString("N"), StringComparer.Ordinal).Take(51).ToArray());
    }
}
