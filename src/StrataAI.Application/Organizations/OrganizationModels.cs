namespace StrataAI.Application.Organizations;

public enum OrganizationRole
{
    Owner,
    Admin,
    Member,
}

public enum OrganizationStatus
{
    Active,
    Archived,
    Deleting,
}

public sealed record OrganizationRecord(
    Guid Id,
    string Name,
    string? Description,
    string? LogoUrl,
    Guid OwnerUserId,
    OrganizationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record OrganizationMembership(
    Guid Id,
    Guid OrganizationId,
    Guid UserId,
    OrganizationRole Role,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record OrganizationSummary(
    OrganizationRecord Organization,
    OrganizationRole Role);

public sealed record OrganizationMemberSummary(Guid MembershipId, Guid UserId, string DisplayName,
    string Email, OrganizationRole Role, StrataAI.Application.Identity.AccountStatus AccountStatus,
    bool EmailVerified, bool IsUsableOwner, DateTimeOffset JoinedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record OrganizationMemberPage(Guid OrganizationId,
    IReadOnlyList<OrganizationMemberSummary> Items, Guid? NextCursor);

public sealed record OrganizationBoardSummary(
    Guid Id,
    string Name,
    long Version);

public sealed record OrganizationOperation<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode)
{
    public static OrganizationOperation<T> Success(T value) =>
        new(true, value, null);

    public static OrganizationOperation<T> Failure(string errorCode) =>
        new(false, default, errorCode);
}
