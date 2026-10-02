namespace StrataAI.Application.WorkManagement;

public sealed record BoardCardFilter(string Keyword, IReadOnlyList<Guid> LabelIds, bool MatchAll);
public sealed record BoardCardFilterPage(Guid OrganizationId, Guid BoardId, IReadOnlyList<CardRecord> Items, Guid? NextCursor);

public sealed record CardLabelChange(CardRecord Card, Guid LabelId, bool Assigned, bool Changed);
public sealed record CardLabelOption(BoardLabelRecord Label, bool Assigned);
public sealed record CardLabelOptionsPage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    IReadOnlyList<CardLabelOption> Items, Guid? NextCursor);
public sealed record CardLabelIndicator(Guid Id, string Name, string Color);
public sealed record CardLabelPreview(IReadOnlyList<CardLabelIndicator> Items, long Total);
public sealed record CardLabelPage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    IReadOnlyList<BoardLabelRecord> Items, Guid? NextCursor, bool CanEdit);

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

public sealed record BoardMemberDirectoryEntry(Guid BoardId, Guid UserId, BoardRole Role,
    bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version,
    string? DisplayName, string? Email, bool OrganizationMemberActive);

public sealed record ArchivedListEntry(BoardListRecord List, long ContainedCardCount);
public sealed record BoardLabelRecord(Guid Id, Guid OrganizationId, Guid BoardId, string Name, string Color,
    string Rank, bool Deleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);
public sealed record BoardLabelPage(Guid OrganizationId, Guid BoardId, IReadOnlyList<BoardLabelRecord> Items,
    Guid? NextCursor, bool CanEdit, bool CanDelete);
public sealed record ArchivedCardEntry(CardRecord Card, BoardListRecord List);
public sealed record ArchivedCardPage(Guid OrganizationId, Guid BoardId,
    IReadOnlyList<ArchivedCardEntry> Items, Guid? NextCursor, bool CanDelete);
public sealed record ArchivedListPage(Guid OrganizationId, Guid BoardId,
    IReadOnlyList<ArchivedListEntry> Items, Guid? NextCursor);

public sealed record BoardSnapshot(
    BoardRecord Board,
    IReadOnlyList<BoardListSnapshot> Lists,
    bool Starred,
    BoardAccess Access,
    IReadOnlyDictionary<Guid, CardLabelPreview>? CardLabels = null);

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
