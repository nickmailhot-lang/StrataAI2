using StrataAI.Application.Common;

namespace StrataAI.Application.Identity;

public enum IdentityRevocationKind { Logout, Deactivate }
public sealed record RevocationSessionProof(Guid UserId, Guid SessionId, DateTimeOffset ExpiresAt,
    bool Revoked, AccountStatus Status, bool EmailVerified);
public sealed record IdentityRevocationReceipt(Guid SessionId, IdentityRevocationKind Kind, DateTimeOffset ExpiresAt);

public interface IIdentityRevocationReplayStore
{
    Task<IdentityRevocationReceipt?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid userId, Guid key, IdentityRevocationReceipt receipt, CancellationToken cancellationToken);
}

// Called only inside the owning identity transaction/gate. This proof grants no general authentication.
public sealed class IdentityRevocationReplayExecutor(IIdentityStore identities, IIdentityRevocationReplayStore receipts,
    IClock clock, IdentityPolicy policy)
{
    public async Task<IdentityOperation<bool>> ExecuteAsync(Guid expectedActor, string sessionHash, Guid key,
        IdentityRevocationKind kind, Func<Guid, Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken)
    {
        if (key == Guid.Empty || !Enum.IsDefined(kind)) return IdentityOperation<bool>.Failure("session_unavailable");
        var proof = await identities.FindRevocationSessionProofAsync(sessionHash, cancellationToken);
        if (proof is null || proof.ExpiresAt <= clock.UtcNow || (expectedActor != Guid.Empty && proof.UserId != expectedActor))
            return IdentityOperation<bool>.Failure("session_unavailable");
        var prior = await receipts.ReadAsync(proof.UserId, key, cancellationToken);
        if (proof.ExpiresAt <= clock.UtcNow) return IdentityOperation<bool>.Failure("session_unavailable");
        if (prior is not null && prior.ExpiresAt > clock.UtcNow)
            return prior.SessionId == proof.SessionId && prior.Kind == kind
                ? IdentityOperation<bool>.Success(true) : IdentityOperation<bool>.Failure("idempotency_key_reused");
        if (proof.Revoked || proof.Status != AccountStatus.Active || (policy.RequireVerifiedEmail && !proof.EmailVerified))
            return IdentityOperation<bool>.Failure("session_unavailable");
        var result = await operation(proof.UserId);
        if (!result.Succeeded) return result;
        var now = clock.UtcNow;
        if (proof.ExpiresAt <= now) return IdentityOperation<bool>.Failure("session_unavailable");
        var expiry = proof.ExpiresAt < now.AddHours(24) ? proof.ExpiresAt : now.AddHours(24);
        await receipts.SaveAsync(proof.UserId, key, new IdentityRevocationReceipt(proof.SessionId, kind, expiry), cancellationToken);
        return result;
    }
}
