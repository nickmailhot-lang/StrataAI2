namespace StrataAI.Application.WorkManagement;

public interface IWorkManagementService
{
    Task<WorkOperation<BoardRecord>> CreateBoardAsync(
        Guid organizationId,
        Guid actorUserId,
        string name,
        string? description,
        BoardVisibility visibility,
        string? backgroundType,
        string? backgroundValue,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<BoardSnapshot>> GetBoardAsync(
        Guid boardId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<ArchivedListPage>> ListArchivedListsAsync(Guid boardId, Guid actorUserId,
        Guid? after = null, CancellationToken cancellationToken = default);

    Task<WorkOperation<BoardRecord>> UpdateBoardAsync(
        Guid boardId,
        Guid actorUserId,
        string name,
        string? description,
        string? backgroundType,
        string? backgroundValue,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<BoardRecord>> SetBoardVisibilityAsync(
        Guid boardId,
        Guid actorUserId,
        BoardVisibility visibility,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<BoardRecord>> ArchiveBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<BoardRecord>> RestoreBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<BoardRecord>> DeleteBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<bool>> SetStarAsync(
        Guid boardId,
        Guid actorUserId,
        bool starred,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<IReadOnlyList<BoardMemberDirectoryEntry>>> ListBoardMembersAsync(
        Guid boardId,
        Guid actorUserId,
        CancellationToken cancellationToken = default, Guid? after = null);

    Task<WorkOperation<BoardMemberRecord>> SetBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        BoardRole role,
        string correlationId,
        CancellationToken cancellationToken = default, long? expectedMemberVersion = null);

    Task<WorkOperation<bool>> RemoveBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default, long? expectedMemberVersion = null);

    Task<WorkOperation<BoardListRecord>> CreateListAsync(
        Guid boardId,
        Guid actorUserId,
        string name,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<BoardListRecord>> UpdateListAsync(
        Guid listId,
        Guid actorUserId,
        string name,
        string? rank,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default, Guid? beforeListId = null, bool moveToEnd = false);

    Task<WorkOperation<BoardListRecord>> SetListLifecycleAsync(
        Guid listId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<CardRecord>> CreateCardAsync(
        Guid listId,
        Guid actorUserId,
        string title,
        string? description,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<CardRecord>> UpdateCardAsync(
        Guid cardId,
        Guid actorUserId,
        string title,
        string? description,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkOperation<CardRecord>> MoveCardAsync(
        Guid cardId,
        Guid actorUserId,
        Guid destinationListId,
        string? rank,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default, Guid? beforeCardId = null);

    Task<WorkOperation<CardRecord>> SetCardLifecycleAsync(
        Guid cardId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);
}
