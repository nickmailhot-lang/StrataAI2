using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryWatchSubscriptionStore : IWatchSubscriptionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid Organization, Guid User, string Type, Guid Entity), WatchSubscription> _rows = new();
    public Task<WatchSubscription?> FindAsync(Guid organizationId, Guid userId, string entityType, Guid entityId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_gate)
            return Task.FromResult(_rows.GetValueOrDefault((organizationId, userId, entityType, entityId)));
    }
    public Task<WatchSubscription?> SetAsync(Guid organizationId, Guid userId, string entityType, Guid entityId,
        bool watching, long expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_gate)
        {
            var key = (organizationId, userId, entityType, entityId); var old = _rows.GetValueOrDefault(key);
            if ((old?.Version ?? 0) != expectedVersion) return Task.FromResult<WatchSubscription?>(null);
            if ((old?.Watching ?? false) == watching) return Task.FromResult(old);
            var row = old is null ? new WatchSubscription(Guid.NewGuid(), organizationId, userId, entityType, entityId, watching, now, now, 1) :
                old with { Watching = watching, UpdatedAt = now < old.UpdatedAt ? old.UpdatedAt : now, Version = old.Version + 1 };
            _rows[key] = row; return Task.FromResult<WatchSubscription?>(row);
        }
    }
}
