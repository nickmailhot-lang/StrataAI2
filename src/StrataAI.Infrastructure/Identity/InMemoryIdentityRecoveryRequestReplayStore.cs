using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed partial class InMemoryIdentityRecoveryRequestReplayStore : IIdentityRecoveryRequestReplayStore
{
    private readonly Dictionary<(Guid User, Guid Key, IdentityTokenPurpose Purpose), IdentityRecoveryRequestReplay> rows = [];
    public Task<IdentityRecoveryRequestReplay?> ReadAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(rows.GetValueOrDefault((userId, key, purpose)));
    }
    public Task SaveAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, IdentityRecoveryRequestReplay replay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rows.Add((userId, key, purpose), replay);
        return Task.CompletedTask;
    }
}
