namespace StrataAI.Application.WorkManagement;

public interface IWorkManagementService
{
    Task<WorkOperation<ArchivedBoardPage>> ListArchivedBoardsAsync(Guid organizationId, Guid actorId,
        Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<BoardSearchPage>> SearchBoardAsync(Guid boardId, GlobalSearchBinding binding,
        Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<CardRecord>> CopyCardAsync(Guid cardId, Guid sourceBoardId, Guid destinationListId,
        Guid actorId, string title, long expectedVersion, string correlationId, CancellationToken cancellationToken = default);
    Task<WorkOperation<CardMemberOptionsPage>> ListCardMemberOptionsAsync(Guid cardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<CardAssigneePage>> ListCardMembersAsync(Guid cardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<CardMemberChange>> SetCardMemberAsync(Guid cardId, Guid userId, Guid actorId, bool assigned, long version, string correlationId, CancellationToken cancellationToken = default);
    Task<WorkOperation<AssignableBoardMemberPage>> ListAssignableBoardMembersAsync(Guid boardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<BoardCardFilterPage>> FilterBoardCardsAsync(Guid boardId, Guid actorId, string? keyword, IReadOnlyList<Guid> labelIds, string? match, Guid? after = null, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? memberIds = null, string? completion = null, string? due = null, string? activity = null);
    Task<WorkOperation<CardLabelOptionsPage>> ListCardLabelOptionsAsync(Guid cardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<BoardLabelRecord>> MoveLabelAsync(Guid labelId, Guid actorId, Guid? beforeLabelId, long version, string correlationId, CancellationToken cancellationToken = default);
    Task<WorkOperation<CardLabelPage>> ListCardLabelsAsync(Guid cardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<CardLabelChange>> SetCardLabelAsync(Guid cardId, Guid labelId, Guid actorId, bool assigned, long version, string correlationId, CancellationToken cancellationToken = default);
    Task<WorkOperation<BoardLabelPage>> ListLabelsAsync(Guid boardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default);
    Task<WorkOperation<BoardLabelRecord>> CreateLabelAsync(Guid boardId, Guid actorId, string name, string color,
        string correlationId, CancellationToken cancellationToken = default);
    Task<WorkOperation<BoardLabelRecord>> UpdateLabelAsync(Guid labelId, Guid actorId, string name, string color,
        string? rank, long version, string correlationId, CancellationToken cancellationToken = default);
    Task<WorkOperation<BoardLabelRecord>> DeleteLabelAsync(Guid labelId, Guid actorId, long version,
        bool confirmed, string correlationId, CancellationToken cancellationToken = default);
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

    Task<WorkOperation<ArchivedCardPage>> ListArchivedCardsAsync(Guid boardId, Guid actorUserId,
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
        CancellationToken cancellationToken = default, bool deletionConfirmed = false);

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

    Task<WorkOperation<BoardListRecord>> CopyListAsync(Guid listId, Guid destinationBoardId,
        Guid actorUserId, string name, long expectedVersion, string correlationId,
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
        CancellationToken cancellationToken = default, bool deletionConfirmed = false, long? expectedContainedCardCount = null);

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
        CancellationToken cancellationToken = default, Guid? beforeCardId = null, Guid? sourceBoardId = null);

    Task<WorkOperation<CardRecord>> SetCardLifecycleAsync(
        Guid cardId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default, bool deletionConfirmed = false);
}
