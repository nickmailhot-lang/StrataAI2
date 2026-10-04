using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;
internal sealed partial class InMemoryIdentityLoginReplayStore : IIdentityLoginReplayStore
{
    private readonly Dictionary<(Guid, Guid), IdentityLoginReplay> _rows = [];
    // The shared identity command gate owns every access.
    public Task<IdentityLoginReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault((userId, key)));
    public Task SaveAsync(Guid userId, Guid key, IdentityLoginReplay replay, CancellationToken cancellationToken)
    { _rows.Add((userId, key), replay); return Task.CompletedTask; }
}
