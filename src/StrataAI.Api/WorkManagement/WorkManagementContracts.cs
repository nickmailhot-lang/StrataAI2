namespace StrataAI.Api.WorkManagement;

public sealed record CreateBoardRequest(
    Guid OrganizationId,
    string Name,
    string? Description,
    string? Visibility,
    string? BackgroundType,
    string? BackgroundValue);

public sealed record UpdateBoardRequest(
    string Name,
    string? Description,
    string? BackgroundType,
    string? BackgroundValue,
    long Version);

public sealed record SetBoardVisibilityRequest(
    string Visibility,
    long Version);

public sealed record VersionRequest(long Version);

public sealed record SetBoardMemberRequest(string Role);

public sealed record CreateListRequest(
    string Name,
    string? Rank);

public sealed record UpdateListRequest(
    string Name,
    string? Rank,
    long Version, Guid? BeforeListId = null, bool MoveToEnd = false);

public sealed record CreateCardRequest(
    string Title,
    string? Description,
    string? Rank);

public sealed record UpdateCardRequest(
    string Title,
    string? Description,
    long Version);

public sealed record MoveCardRequest(
    Guid DestinationListId,
    string? Rank,
    long ExpectedVersion,
    Guid? BeforeCardId = null);
