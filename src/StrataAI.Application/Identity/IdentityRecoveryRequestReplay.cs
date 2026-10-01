namespace StrataAI.Application.Identity;

public enum RecoveryTokenSource { ApiRequest, EmailDelivery }
public sealed record IdentityRecoveryRequestReplay(string KeyVersion, string Fingerprint, Guid TokenId,
    RecoveryTokenSource TokenSource, string TokenKeyVersion, DateTimeOffset ExpiresAt);
public interface IIdentityRecoveryRequestReplayStore
{
    Task<IdentityRecoveryRequestReplay?> ReadAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, CancellationToken cancellationToken);
    Task SaveAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, IdentityRecoveryRequestReplay replay, CancellationToken cancellationToken);
}
public interface IIdentityRecoveryRetrySecrets
{
    string CurrentKeyVersion { get; }
    bool TryDeriveRecovery(Guid userId, Guid tokenId, IdentityTokenPurpose purpose, string version, out string token);
    bool TryRecoveryFingerprint(Guid userId, Guid key, IdentityTokenPurpose purpose, string emailNormalized, string version, out string fingerprint);
}
