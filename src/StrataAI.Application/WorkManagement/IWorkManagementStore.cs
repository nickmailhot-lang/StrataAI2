namespace StrataAI.Application.WorkManagement;

public interface IWorkManagementStore
{
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
        CancellationToken cancellationToken = default);

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
        string rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<BoardListRecord?> FindListAsync(
        Guid listId,
        CancellationToken cancellationToken = default);

    Task<BoardListRecord?> UpdateListAsync(
        Guid listId,
        string name,
        string rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

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
        string rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<CardRecord?> FindCardAsync(
        Guid cardId,
        CancellationToken cancellationToken = default);

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
        string rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

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
