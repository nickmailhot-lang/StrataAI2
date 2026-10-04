using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed class InMemoryIdentityHandleClaimReplayStore(DemoIdentityTransactionScope scope, IClock clock, DemoMentionHandleRegistry registry)
    : IIdentityHandleClaimReplayStore, IDemoIdentityTransactionParticipant
{
    private readonly Dictionary<(Guid User,Guid Key),IdentityHandleClaimReplay> _rows = [];
    private void RequireScope(Guid user, Guid key)
    {
        if (!scope.Owns(user)) throw new InvalidOperationException("Handle claim retries require an owning identity subject transaction.");
        if (key == Guid.Empty) throw new ArgumentException("Handle claim retry key is required.");
    }
    public Task<IdentityHandleClaimReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope(userId, key); cancellationToken.ThrowIfCancellationRequested();
        var found = _rows.GetValueOrDefault((userId, key));
        return Task.FromResult(found is null ? null : found with { Expired = found.ExpiresAt <= clock.UtcNow });
    }
    public Task<bool> TrySaveAsync(Guid userId, Guid key, string fingerprint, HandleClaimReceipt receipt, CancellationToken cancellationToken)
    {
        RequireScope(userId, key); cancellationToken.ThrowIfCancellationRequested(); IdentityHandleClaimReplayShape.Require(fingerprint, receipt);
        if (registry.Find(userId) is null) return Task.FromResult(false);
        // Keep an existing original key immutable, including until expired
        // maintenance removes it. Demo hosts bound their process-local cache.
        if (_rows.ContainsKey((userId, key))) return Task.FromResult(false);
        var now = clock.UtcNow; var utc = now.ToUniversalTime(); var at = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        foreach (var expired in _rows.Where(pair => pair.Value.ExpiresAt <= now).Take(100).Select(pair => pair.Key).ToArray()) _rows.Remove(expired);
        if (_rows.Count >= 10000) return Task.FromResult(false);
        _rows.Add((userId, key), new(fingerprint, receipt, at, at.AddHours(24), false));
        return Task.FromResult(true);
    }
    public Action CaptureRollback() => DemoIdentityRollback.Dictionary(_rows);
}
