using StrataAI.Application.Identity;
using StrataAI.Application.Common;

namespace StrataAI.Infrastructure.Identity;

internal sealed partial class InMemoryIdentityRevocationReplayStore(IClock clock) : IIdentityRevocationReplayStore
{
    private readonly Dictionary<(Guid, Guid), IdentityRevocationReceipt> _receipts = [];
    public Task<IdentityRevocationReceipt?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken) =>
        Task.FromResult(_receipts.TryGetValue((userId, key), out var receipt) && receipt.ExpiresAt > clock.UtcNow ? receipt : null);
    public Task SaveAsync(Guid userId, Guid key, IdentityRevocationReceipt receipt, CancellationToken cancellationToken)
    {
        _receipts[(userId, key)] = receipt;
        return Task.CompletedTask;
    }
}
