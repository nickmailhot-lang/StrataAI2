namespace StrataAI.Application.Identity;

public interface IIdentityStore
{
    Task<bool> TryCreateUserAsync(
        UserIdentity user,
        CancellationToken cancellationToken = default);

    Task<UserIdentity?> FindUserByNormalizedEmailAsync(
        string emailNormalized,
        CancellationToken cancellationToken = default);

    Task<UserIdentity?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task CreateSessionAsync(
        SessionRecord session,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedSession?> FindActiveSessionAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    Task CreatePasswordResetTokenAsync(
        SecurityTokenRecord token,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetPasswordResetUserIdAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> CompletePasswordResetAsync(
        string tokenHash,
        string newPasswordHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default);

    Task CreateEmailVerificationTokenAsync(
        SecurityTokenRecord token,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetEmailVerificationUserIdAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyEmailAsync(
        string tokenHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default);

    Task<UserIdentity?> UpdateProfileAsync(
        Guid userId,
        string displayName,
        string? avatarUrl,
        string locale,
        string timezone,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateUserAsync(
        Guid userId,
        DateTimeOffset deactivatedAt,
        CancellationToken cancellationToken = default);

    Task AppendAuditAsync(
        Guid? actorId,
        string eventType,
        string entityType,
        Guid? entityId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
