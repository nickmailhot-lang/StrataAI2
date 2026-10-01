namespace StrataAI.Application.Identity;

public interface IIdentityCommandContext
{
    Guid? IdempotencyKey { get; }
    string? RevocationSessionTokenHash { get; }
}

// This typed store cannot accept credential-bearing login/registration/token outcomes.
public sealed record IdentityProfileReplay(string Fingerprint, UserProfile Profile);

public interface IIdentityProfileReplayStore
{
    Task<IdentityProfileReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid userId, Guid key, IdentityProfileReplay replay, CancellationToken cancellationToken);
}
