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
    DateTimeOffset? RevokedAt,
    Guid? AcceptedByUserId = null,
    string? OrganizationName = null,
    BoardInvitationTarget? BoardTarget = null);

public sealed record BoardInvitationTarget(Guid BoardId, StrataAI.Application.WorkManagement.BoardRole Role);

public sealed record PendingInvitation(
    Guid Id,
    Guid OrganizationId,
    InvitationSurface Surface,
    string TargetRole,
    DateTimeOffset ExpiresAt,
    string OrganizationName,
    BoardInvitationTarget? BoardTarget = null,
    string? BoardName = null);

public sealed record CreatedInvitation(
    InvitationRecord Invitation,
    string RawToken);

public sealed record InvitationCreationReplay(string Fingerprint, bool Expired, InvitationRecord Invitation);

public sealed record PendingInvitationPage(IReadOnlyList<PendingInvitation> Items, Guid? NextCursor);

public sealed record AcceptedInvitation(
    Guid InvitationId,
    Guid OrganizationId,
    InvitationSurface Surface,
    string TargetRole,
    BoardInvitationTarget? BoardTarget = null);

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
