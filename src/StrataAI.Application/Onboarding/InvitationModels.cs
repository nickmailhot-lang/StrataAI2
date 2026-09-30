namespace StrataAI.Application.Onboarding;

public enum InvitationSurface
{
    Internal,
    Portal,
}

public sealed record InvitationRecord(
    Guid Id,
    Guid OrganizationId,
    string InvitedEmail,
    string EmailNormalized,
    string TokenHash,
    InvitationSurface Surface,
    string TargetRole,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt);

public sealed record PendingInvitation(
    Guid Id,
    Guid OrganizationId,
    InvitationSurface Surface,
    string TargetRole,
    DateTimeOffset ExpiresAt);

public sealed record CreatedInvitation(
    InvitationRecord Invitation,
    string RawToken);

public sealed record AcceptedInvitation(
    Guid InvitationId,
    Guid OrganizationId,
    InvitationSurface Surface,
    string TargetRole);

public sealed record InvitationOperation<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode)
{
    public static InvitationOperation<T> Success(T value) =>
        new(true, value, null);

    public static InvitationOperation<T> Failure(string errorCode) =>
        new(false, default, errorCode);
}
