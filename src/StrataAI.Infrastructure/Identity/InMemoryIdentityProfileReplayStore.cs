using StrataAI.Application.Identity;
using StrataAI.Application.Common;

namespace StrataAI.Infrastructure.Identity;

internal sealed partial class InMemoryIdentityProfileReplayStore(IClock clock) : IIdentityProfileReplayStore
{
    private readonly Dictionary<(Guid, Guid), (IdentityProfileReplay Replay, DateTimeOffset Expires)> _records = [];
    public Task<IdentityProfileReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken)
    {
        // The owning demo identity command gate serializes accesses.
        if (_records.TryGetValue((userId, key), out var record) && record.Expires > clock.UtcNow)
            return Task.FromResult<IdentityProfileReplay?>(record.Replay);
        _records.Remove((userId, key));
        return Task.FromResult<IdentityProfileReplay?>(null);
    }
    public Task SaveAsync(Guid userId, Guid key, IdentityProfileReplay replay, CancellationToken cancellationToken)
    {
        _records[(userId, key)] = (replay, clock.UtcNow.AddHours(24));
        return Task.CompletedTask;
    }
}
