using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed class InMemoryIdentityTokenConsumptionReplayStore : IIdentityTokenConsumptionReplayStore
{
    private readonly Dictionary<(Guid User, Guid Key, IdentityTokenPurpose Purpose), IdentityTokenConsumptionReplay> rows = [];
    public Task<IdentityTokenConsumptionReplay?> ReadAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(rows.GetValueOrDefault((userId, key, purpose)));
    }
    public Task SaveAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, IdentityTokenConsumptionReplay replay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rows.Add((userId, key, purpose), replay);
        return Task.CompletedTask;
    }
}
