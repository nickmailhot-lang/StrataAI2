namespace StrataAI.Application.Identity;

public sealed record IdentitySecurityTokenProof(UserIdentity User, Guid TokenId, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt);
public sealed record IdentityTokenConsumptionReplay(Guid TokenId, string KeyVersion, string Fingerprint, DateTimeOffset ConsumedAt, DateTimeOffset ExpiresAt);
public interface IIdentityTokenConsumptionReplayStore
{
    Task<IdentityTokenConsumptionReplay?> ReadAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, CancellationToken cancellationToken);
    Task SaveAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, IdentityTokenConsumptionReplay replay, CancellationToken cancellationToken);
}
public interface IIdentityTokenConsumptionRetrySecrets
{
    string CurrentKeyVersion { get; }
    bool TryConsumptionFingerprint(Guid userId, Guid key, IdentityTokenPurpose purpose, string rawToken, string? newPassword, string version, out string fingerprint);
}
