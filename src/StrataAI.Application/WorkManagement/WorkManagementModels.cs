namespace StrataAI.Application.WorkManagement;

public enum BoardVisibility
{
    Private,
    Organization,
    Public,
}

public enum BoardLifecycleState
{
    Active,
    Archived,
    Deleted,
}

public enum BoardRole
{
    Admin,
    Member,
}

public enum WorkItemLifecycleState
{
    Active,
    Archived,
    Deleted,
}

public sealed record BoardRecord(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description,
    BoardVisibility Visibility,
    string BackgroundType,
    string? BackgroundValue,
    BoardLifecycleState LifecycleState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record BoardListRecord(
    Guid Id,
    Guid OrganizationId,
    Guid BoardId,
    string Name,
    string Rank,
    WorkItemLifecycleState LifecycleState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record CardRecord(
    Guid Id,
    Guid OrganizationId,
    Guid BoardId,
    Guid ListId,
    string Title,
    string? Description,
    string Rank,
    WorkItemLifecycleState LifecycleState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record BoardMemberRecord(
    Guid BoardId,
    Guid UserId,
    BoardRole Role,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record BoardSnapshot(
    BoardRecord Board,
    IReadOnlyList<BoardListSnapshot> Lists,
    bool Starred,
    BoardAccess Access);

public sealed record BoardListSnapshot(
    BoardListRecord List,
    IReadOnlyList<CardRecord> Cards);

public sealed record BoardAccess(
    bool CanView,
    bool CanEdit,
    bool CanAdminister,
    bool CanMove);

public sealed record WorkOperation<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode)
{
    public static WorkOperation<T> Success(T value) => new(true, value, null);

    public static WorkOperation<T> Failure(string errorCode) =>
        new(false, default, errorCode);
}
