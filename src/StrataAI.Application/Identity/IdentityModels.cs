namespace StrataAI.Application.Identity;

public enum AccountStatus
{
    PendingVerification,
    Active,
    Suspended,
    Deactivated,
}

public sealed record UserIdentity(
    Guid Id,
    string Email,
    string EmailNormalized,
    string DisplayName,
    string? AvatarUrl,
    string Locale,
    string Timezone,
    AccountStatus Status,
    bool EmailVerified,
    string PasswordHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record UserProfile(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string Locale,
    string Timezone,
    AccountStatus Status,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record SessionRecord(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public DateTimeOffset UpdatedAt { get; init; } = CreatedAt;
}

public sealed record AuthenticatedSession(
    Guid SessionId,
    UserIdentity User,
    DateTimeOffset ExpiresAt);

public sealed record SecurityTokenRecord(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public DateTimeOffset UpdatedAt { get; init; } = CreatedAt;
}

public sealed record IdentityPolicy(
    bool AllowSelfRegistration,
    bool RequireVerifiedEmail,
    int MinimumPasswordLength,
    TimeSpan SessionLifetime,
    TimeSpan SecurityTokenLifetime,
    bool EmailDeliveryEnabled = false);

public sealed record RegistrationOutcome(
    UserProfile User,
    string? VerificationToken);

public sealed record LoginOutcome(
    UserProfile User,
    string SessionToken,
    DateTimeOffset SessionExpiresAt);

public sealed record PasswordResetRequestOutcome(
    string? ResetToken);

public sealed record IdentityDomainEvent(Guid EventId, long Sequence, string EventType, Guid ActorId,
    Guid? OrganizationId, Guid? BoardId, string EntityType, Guid EntityId, long Version,
    IReadOnlyDictionary<string, string> Metadata, string CorrelationId, DateTimeOffset CreatedAt);

public sealed record IdentitySyncSnapshot(UserProfile Profile, long Cursor, long LatestSequence,
    bool HasMore, IReadOnlyList<IdentityDomainEvent> Events);
public sealed record IdentityEventPage(long Cursor, long LatestSequence,
    bool HasMore, IReadOnlyList<IdentityDomainEvent> Events);

public sealed record IdentityOperation<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode)
{
    public static IdentityOperation<T> Success(T value) => new(true, value, null);

    public static IdentityOperation<T> Failure(string errorCode) =>
        new(false, default, errorCode);
}
