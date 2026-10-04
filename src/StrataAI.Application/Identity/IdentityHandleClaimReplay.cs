namespace StrataAI.Application.Identity;

// Deliberately excludes handle/profile/credential text. Application must hydrate
// only a current authorized handle at the acknowledged revision.
public sealed record HandleClaimReceipt(long UserVersion, long HandleVersion, bool Changed);
public sealed record IdentityHandleClaimReplay(string Fingerprint, HandleClaimReceipt Receipt,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Expired);
public interface IIdentityHandleClaimReplayStore
{
    Task<IdentityHandleClaimReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken);
    Task<bool> TrySaveAsync(Guid userId, Guid key, string fingerprint, HandleClaimReceipt receipt, CancellationToken cancellationToken);
}
