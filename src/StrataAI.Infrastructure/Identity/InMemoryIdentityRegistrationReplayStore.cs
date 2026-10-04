using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed partial class InMemoryIdentityRegistrationReplayStore : IIdentityRegistrationReplayStore
{
    private readonly Dictionary<(Guid UserId, Guid Key), IdentityRegistrationReplay> _rows = [];
    // Every access belongs to the shared identity command gate.
    public Task<IdentityRegistrationReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault((userId, key)));
    public Task SaveAsync(Guid userId, Guid key, IdentityRegistrationReplay replay, CancellationToken cancellationToken)
    { _rows.Add((userId, key), replay); return Task.CompletedTask; }
}
