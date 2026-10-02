namespace StrataAI.Application.WorkManagement;

public interface IWorkManagementStore
{
    Task<IReadOnlyList<CardLabelOption>> ListCardLabelOptionsAsync(Guid cardId, Guid? after, CancellationToken cancellationToken = default);
    Task<BoardLabelRecord?> MoveLabelAsync(Guid labelId, Guid? beforeLabelId, long version, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BoardLabelRecord>> ListCardLabelsAsync(Guid cardId, Guid? after, CancellationToken cancellationToken = default);
    Task<CardLabelChange?> SetCardLabelAsync(Guid cardId, Guid labelId, bool assigned, long version, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BoardLabelRecord>> ListLabelsAsync(Guid boardId, Guid? after, CancellationToken cancellationToken = default);
    Task<BoardLabelRecord?> FindLabelAsync(Guid labelId, CancellationToken cancellationToken = default, bool includeDeleted = false);
    Task<BoardLabelRecord> CreateLabelAsync(Guid boardId, Guid id, string name, string color, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<BoardLabelRecord?> UpdateLabelAsync(Guid labelId, string name, string color, string rank, long version, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<BoardLabelRecord?> DeleteLabelAsync(Guid labelId, long version, DateTimeOffset now, CancellationToken cancellationToken = default);
    // Production holds these locks in the owning command transaction. Re-read
    // authorization and lifecycle after this method, before changing any entity.
    Task<bool> AcquireCommandScopeAsync(Guid organizationId, Guid actorId,
        Guid? boardId, CancellationToken cancellationToken = default);

    // Caller must first verify active organization membership.
    Task<IReadOnlyList<StrataAI.Application.Organizations.OrganizationBoardSummary>> ListVisibleBoardsAsync(
        Guid organizationId,
        Guid userId,
        bool organizationAdministrator,
        CancellationToken cancellationToken = default);

    Task<BoardRecord> CreateBoardAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid boardId,
        string name,
        string? description,
        BoardVisibility visibility,
        string backgroundType,
        string? backgroundValue,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<BoardRecord?> FindBoardAsync(
        Guid boardId,
        CancellationToken cancellationToken = default);

    Task<BoardMemberRecord?> FindBoardMemberAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<BoardSnapshot?> GetSnapshotAsync(
        Guid boardId,
        Guid? userId,
        BoardAccess access,
        CancellationToken cancellationToken = default);

    // Caller must hold current Board administration authority in its command scope.
    Task<IReadOnlyList<ArchivedListEntry>> ListArchivedListsAsync(Guid boardId, Guid? after,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchivedCardEntry>> ListArchivedCardsAsync(Guid boardId, Guid? after,
        CancellationToken cancellationToken = default);

    Task<BoardRecord?> UpdateBoardAsync(
        Guid boardId,
        string name,
        string? description,
        string backgroundType,
        string? backgroundValue,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<BoardRecord?> SetBoardVisibilityAsync(
        Guid boardId,
        BoardVisibility visibility,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<BoardRecord?> SetBoardLifecycleAsync(
        Guid boardId,
        BoardLifecycleState expectedState,
        BoardLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task SetStarAsync(
        Guid boardId,
        Guid userId,
        bool starred,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BoardMemberRecord>> ListBoardMembersAsync(
        Guid boardId,
        CancellationToken cancellationToken = default,
        Guid? after = null, int? limit = null);

    Task<BoardMemberRecord> UpsertBoardMemberAsync(
        Guid boardId,
        Guid userId,
        BoardRole role,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveBoardMemberAsync(
        Guid boardId,
        Guid userId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<BoardListRecord> CreateListAsync(
        Guid boardId,
        Guid listId,
        string name,
        string? rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    // Owning command holds both Board scopes. Copies every non-deleted Card;
    // timestamps/IDs/versions are new while order and archive state are preserved.
    Task<BoardListRecord> CopyListAsync(Guid sourceListId, Guid destinationBoardId,
        Guid copiedListId, string name, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<BoardListRecord?> FindListAsync(
        Guid listId,
        CancellationToken cancellationToken = default, bool includeDeleted = false);

    Task<long> CountContainedCardsAsync(Guid listId, CancellationToken cancellationToken = default);

    Task<BoardListRecord?> UpdateListAsync(
        Guid listId,
        string name,
        string rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? beforeListId = null, bool moveToEnd = false);

    Task<BoardListRecord?> SetListLifecycleAsync(
        Guid listId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<CardRecord> CreateCardAsync(
        Guid listId,
        Guid cardId,
        string title,
        string? description,
        string? rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<CardRecord?> FindCardAsync(
        Guid cardId,
        CancellationToken cancellationToken = default, bool includeDeleted = false);

    Task<CardRecord?> UpdateCardAsync(
        Guid cardId,
        string title,
        string? description,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<CardRecord?> MoveCardAsync(
        Guid cardId,
        Guid destinationListId,
        string? rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? beforeCardId = null);

    Task<CardRecord?> SetCardLifecycleAsync(
        Guid cardId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task AppendAuditAsync(
        Guid organizationId,
        Guid actorUserId,
        string eventType,
        string entityType,
        Guid entityId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
