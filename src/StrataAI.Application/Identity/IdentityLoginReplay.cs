namespace StrataAI.Application.Identity;

// Only immutable session coordinates and a keyed intent digest; no password, cookie or response JSON.
public sealed record IdentityLoginReplay(Guid SessionId, string KeyVersion, string Fingerprint, DateTimeOffset ExpiresAt);
public interface IIdentityLoginReplayStore
{
    Task<IdentityLoginReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid userId, Guid key, IdentityLoginReplay replay, CancellationToken cancellationToken);
}
public interface IIdentityLoginRetrySecrets
{
    string CurrentKeyVersion { get; }
    bool TryDeriveSession(Guid userId, Guid sessionId, string keyVersion, out string token);
    bool TryFingerprint(Guid userId, Guid intentKey, string emailNormalized, string password, string keyVersion, out string fingerprint);
}
