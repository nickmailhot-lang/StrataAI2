namespace StrataAI.Application.WorkManagement;

public interface IWorkManagementStore
{
    Task<IReadOnlyList<ArchivedBoardSummary>> ListArchivedBoardsAsync(Guid organizationId, Guid actorId,
        bool organizationAdministrator, Guid? after, CancellationToken cancellationToken = default);
    Task<CardRecord?> CopyCardAsync(Guid sourceCardId, Guid destinationListId, Guid copiedCardId,
        string title, long expectedVersion, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListReminderCandidateBoardIdsAsync(Guid organizationId, CancellationToken ct);
    // Internal lifecycle candidates, including archived/deleted children. No
    // UI pagination/cap may truncate Reminder cancellation or renewal.
    Task<IReadOnlyList<Guid>> ListReminderCandidateCardIdsAsync(Guid organizationId, Guid boardId, Guid? listId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, CardMemberPreview>> ListCardMemberPreviewsAsync(Guid boardId, bool requireVerifiedEmail, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CardMemberOption>> ListCardMemberOptionsAsync(Guid cardId, Guid? after, bool requireVerifiedEmail, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CardAssignee>> ListCardMembersAsync(Guid cardId, Guid? after, bool requireVerifiedEmail, CancellationToken cancellationToken = default);
    Task<bool> IsAssignableBoardMemberAsync(Guid boardId, Guid userId, bool requireVerifiedEmail, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CardRecord>> RemoveBoardCardMemberAssignmentsAsync(Guid boardId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CardRecord>> RemoveOrganizationCardMemberAssignmentsAsync(Guid organizationId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<CardMemberChange?> SetCardMemberAsync(Guid cardId, Guid userId, Guid actorId, bool assigned, long version, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssignableBoardMember>> ListAssignableBoardMembersAsync(Guid boardId, Guid? after, bool requireVerifiedEmail, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CardRecord>> FilterBoardCardsAsync(Guid boardId, BoardCardFilter filter, Guid? after, CancellationToken cancellationToken = default);
    // Owning authorized Board transaction required. Search predicates and
    // lifecycle scope apply before UUID seek / 51-row lookahead.
    Task<IReadOnlyList<CardRecord>> SearchBoardCardsAsync(Guid boardId, GlobalSearchBinding binding, bool requireVerifiedEmail,
        Guid? after, CancellationToken cancellationToken = default);
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

    // Snapshot reads keep the same parent/member locks but admit an archived
    // Organization. The caller must freshly authorize view access afterward.
    Task<bool> AcquireBoardReadScopeAsync(Guid organizationId, Guid actorId,
        Guid boardId, CancellationToken cancellationToken = default);
    Task<bool> AcquireOrganizationReadScopeAsync(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default);

    // Caller must first verify active organization membership.
    Task<IReadOnlyList<StrataAI.Application.Organizations.OrganizationBoardSummary>> ListVisibleBoardsAsync(
        Guid organizationId,
        Guid userId,
        bool organizationAdministrator,
        CancellationToken cancellationToken = default);

    // Caller must freshly authorize the owning Organization. UUID seek,
    // 51 rows; Board admission is still required before reading Card content.
    Task<IReadOnlyList<StrataAI.Application.Organizations.OrganizationBoardSummary>> ListVisibleBoardsPageAsync(
        Guid organizationId, Guid userId, bool organizationAdministrator, Guid? after, CancellationToken cancellationToken = default);

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
        CancellationToken cancellationToken = default, bool includeDeleted = false);

    Task<BoardMemberRecord?> FindBoardMemberAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default, bool includeDeleted = false);

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
        CancellationToken cancellationToken = default, Guid? actorUserId = null);

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
        CancellationToken cancellationToken = default, Guid? actorUserId = null);

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
        CancellationToken cancellationToken = default, Guid? beforeCardId = null, bool requireVerifiedEmail = false);

    Task<CardRecord?> SetCardLifecycleAsync(
        Guid cardId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? actorUserId = null);

    Task AppendAuditAsync(
        Guid organizationId,
        Guid actorUserId,
        string eventType,
        string entityType,
        Guid entityId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
