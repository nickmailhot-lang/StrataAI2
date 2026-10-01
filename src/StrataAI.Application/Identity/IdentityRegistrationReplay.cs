namespace StrataAI.Application.Identity;

// The registration receipt stores coordinates, never the submitted password or verification bearer.
public enum RegistrationVerificationSource { None, ApiRegistration, EmailDelivery }
public sealed record IdentityRegistrationReplay(string KeyVersion, string Fingerprint,
    Guid? VerificationTokenId, RegistrationVerificationSource VerificationSource,
    string? VerificationKeyVersion, DateTimeOffset ExpiresAt);

public interface IIdentityRegistrationReplayStore
{
    Task<IdentityRegistrationReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid userId, Guid key, IdentityRegistrationReplay replay, CancellationToken cancellationToken);
}

public interface IIdentityRegistrationRetrySecrets
{
    string CurrentKeyVersion { get; }
    bool TryDeriveVerification(Guid userId, Guid tokenId, string keyVersion, out string token);
    bool TryRegistrationFingerprint(Guid userId, Guid key, string emailNormalized, string password,
        string displayName, string locale, string timezone, string keyVersion, out string fingerprint);
}
