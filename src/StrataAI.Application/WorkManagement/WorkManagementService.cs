using StrataAI.Application.Common;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed class WorkManagementService(
    IWorkManagementStore store,
    IOrganizationStore organizationStore,
    IClock clock, IWorkEventStore events) : IWorkManagementService, IWorkBoardAuthorization
{
    public async Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid boardId, Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var current = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        return current is { Access.CanView: true }
            ? WorkOperation<BoardSyncScope>.Success(new(current.Value.Board, current.Value.Access))
            : WorkOperation<BoardSyncScope>.Failure("board_not_found");
    }

    public async Task<WorkOperation<BoardRecord>> CreateBoardAsync(
        Guid organizationId,
        Guid actorUserId,
        string name,
        string? description,
        BoardVisibility visibility,
        string? backgroundType,
        string? backgroundValue,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await organizationStore.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null || !membership.Active)
        {
            return WorkOperation<BoardRecord>.Failure("organization_not_found");
        }

        if (!TryNormalizeName(name, out var normalizedName))
        {
            return WorkOperation<BoardRecord>.Failure("invalid_board_name");
        }

        if (!TryNormalizeBackground(
                backgroundType,
                backgroundValue,
                out var normalizedType,
                out var normalizedValue))
        {
            return WorkOperation<BoardRecord>.Failure("invalid_background");
        }

        var now = clock.UtcNow;
        var board = await store.CreateBoardAsync(
            organizationId,
            actorUserId,
            Guid.NewGuid(),
            normalizedName,
            NormalizeOptional(description),
            visibility,
            normalizedType,
            normalizedValue,
            now,
            cancellationToken);

        await AuditAsync(
            board,
            actorUserId,
            "BOARD_CREATED",
            correlationId,
            cancellationToken);

        return WorkOperation<BoardRecord>.Success(board);
    }

    public async Task<WorkOperation<BoardSnapshot>> GetBoardAsync(
        Guid boardId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (access is null || !access.Value.Access.CanView)
        {
            return WorkOperation<BoardSnapshot>.Failure("board_not_found");
        }

        var snapshot = await store.GetSnapshotAsync(
            boardId,
            actorUserId,
            access.Value.Access,
            cancellationToken);

        return snapshot is null
            ? WorkOperation<BoardSnapshot>.Failure("board_not_found")
            : WorkOperation<BoardSnapshot>.Success(snapshot);
    }

    // PRD-07-FR-009 / PRD-18-FR-004: an archive is distinct from active canvas data.
    public async Task<WorkOperation<ArchivedListPage>> ListArchivedListsAsync(Guid boardId, Guid actorUserId,
        Guid? after = null, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAccessAsync(boardId, actorUserId, cancellationToken);
        if (resolved is null || !resolved.Value.Access.CanAdminister ||
            resolved.Value.Board.LifecycleState != BoardLifecycleState.Active)
            return WorkOperation<ArchivedListPage>.Failure("board_not_found");
        if (after == Guid.Empty) return WorkOperation<ArchivedListPage>.Failure("invalid_archive_cursor");
        var rows = await store.ListArchivedListsAsync(boardId, after, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<ArchivedListPage>.Success(new(resolved.Value.Board.OrganizationId,
            boardId, items, rows.Count > 50 ? items[^1].List.Id : null));
    }

    public async Task<WorkOperation<BoardRecord>> UpdateBoardAsync(
        Guid boardId,
        Guid actorUserId,
        string name,
        string? description,
        string? backgroundType,
        string? backgroundValue,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null ||
            !resolved.Value.Access.CanEdit ||
            resolved.Value.Board.LifecycleState != BoardLifecycleState.Active)
        {
            return WorkOperation<BoardRecord>.Failure("board_not_found");
        }

        if (!TryNormalizeName(name, out var normalizedName))
        {
            return WorkOperation<BoardRecord>.Failure("invalid_board_name");
        }

        var requestedBackgroundType =
            backgroundType ?? resolved.Value.Board.BackgroundType;
        var requestedBackgroundValue =
            backgroundType is null && backgroundValue is null
                ? resolved.Value.Board.BackgroundValue
                : backgroundValue;

        if (!TryNormalizeBackground(
                requestedBackgroundType,
                requestedBackgroundValue,
                out var normalizedType,
                out var normalizedValue))
        {
            return WorkOperation<BoardRecord>.Failure("invalid_background");
        }

        var updated = await store.UpdateBoardAsync(
            boardId,
            normalizedName,
            NormalizeOptional(description),
            normalizedType,
            normalizedValue,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return WorkOperation<BoardRecord>.Failure("version_conflict");
        }

        await AuditAsync(
            updated,
            actorUserId,
            "BOARD_UPDATED",
            correlationId,
            cancellationToken);

        return WorkOperation<BoardRecord>.Success(updated);
    }

    public async Task<WorkOperation<BoardRecord>> SetBoardVisibilityAsync(
        Guid boardId,
        Guid actorUserId,
        BoardVisibility visibility,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null || !resolved.Value.Access.CanAdminister)
        {
            return WorkOperation<BoardRecord>.Failure("board_not_found");
        }

        var updated = await store.SetBoardVisibilityAsync(
            boardId,
            visibility,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return WorkOperation<BoardRecord>.Failure("version_conflict");
        }

        await AuditAsync(
            updated,
            actorUserId,
            "BOARD_VISIBILITY_CHANGED",
            correlationId,
            cancellationToken);

        return WorkOperation<BoardRecord>.Success(updated);
    }

    public Task<WorkOperation<BoardRecord>> ArchiveBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ChangeBoardLifecycleAsync(
            boardId,
            actorUserId,
            BoardLifecycleState.Active,
            BoardLifecycleState.Archived,
            expectedVersion,
            "BOARD_ARCHIVED",
            correlationId,
            cancellationToken);

    public Task<WorkOperation<BoardRecord>> RestoreBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ChangeBoardLifecycleAsync(
            boardId,
            actorUserId,
            BoardLifecycleState.Archived,
            BoardLifecycleState.Active,
            expectedVersion,
            "BOARD_RESTORED",
            correlationId,
            cancellationToken);

    public Task<WorkOperation<BoardRecord>> DeleteBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ChangeBoardLifecycleAsync(
            boardId,
            actorUserId,
            BoardLifecycleState.Archived,
            BoardLifecycleState.Deleted,
            expectedVersion,
            "BOARD_DELETED",
            correlationId,
            cancellationToken);

    public async Task<WorkOperation<bool>> SetStarAsync(
        Guid boardId,
        Guid actorUserId,
        bool starred,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null || !resolved.Value.Access.CanView)
        {
            return WorkOperation<bool>.Failure("board_not_found");
        }

        await store.SetStarAsync(
            boardId,
            actorUserId,
            starred,
            clock.UtcNow,
            cancellationToken);

        return WorkOperation<bool>.Success(starred);
    }

    public async Task<WorkOperation<IReadOnlyList<BoardMemberDirectoryEntry>>> ListBoardMembersAsync(
        Guid boardId,
        Guid actorUserId,
        CancellationToken cancellationToken = default, Guid? after = null)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null || !resolved.Value.Access.CanAdminister)
        {
            return WorkOperation<IReadOnlyList<BoardMemberDirectoryEntry>>.Failure(
                "board_not_found");
        }

        var members = await store.ListBoardMembersAsync(boardId, cancellationToken, after, 51);
        var profiles = members.Count == 0 ? new Dictionary<Guid, OrganizationMemberSummary>()
            : (await organizationStore.ListActiveMembersAsync(resolved.Value.Board.OrganizationId,
                null, cancellationToken, userIds: members.Select(member => member.UserId).ToArray()))
                .ToDictionary(profile => profile.UserId);
        var entries = new List<BoardMemberDirectoryEntry>();
        foreach (var member in members)
        {
            profiles.TryGetValue(member.UserId, out var profile);
            entries.Add(new(member.BoardId, member.UserId, member.Role, member.Active, member.CreatedAt,
                member.UpdatedAt, member.Version, profile?.DisplayName, profile?.Email, profile is not null));
        }
        return WorkOperation<IReadOnlyList<BoardMemberDirectoryEntry>>.Success(entries);
    }

    public async Task<WorkOperation<BoardMemberRecord>> SetBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        BoardRole role,
        string correlationId,
        CancellationToken cancellationToken = default, long? expectedMemberVersion = null)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null || !resolved.Value.Access.CanAdminister)
        {
            return WorkOperation<BoardMemberRecord>.Failure("board_not_found");
        }

        var targetOrganizationMembership =
            await organizationStore.FindMembershipAsync(
                resolved.Value.Board.OrganizationId,
                targetUserId,
                cancellationToken);

        if (targetOrganizationMembership is null ||
            !targetOrganizationMembership.Active)
        {
            return WorkOperation<BoardMemberRecord>.Failure(
                "member_not_eligible");
        }

        if (expectedMemberVersion is not null)
        {
            var currentMember = await store.FindBoardMemberAsync(boardId, targetUserId, cancellationToken);
            if (expectedMemberVersion <= 0 || currentMember is not { Active: true } || currentMember.Version != expectedMemberVersion)
                return WorkOperation<BoardMemberRecord>.Failure("version_conflict");
        }
        // PERM-FR-005/006: changing the role must retain the same safeguard as
        // removal. The existing Organization-admin override remains explicit.
        if (role != BoardRole.Admin && !IsOrganizationAdmin(resolved.Value.OrganizationMembership))
        {
            var members = await store.ListBoardMembersAsync(boardId, cancellationToken);
            if (members.Any(member => member.UserId == targetUserId && member.Active && member.Role == BoardRole.Admin)
                && members.Count(member => member.Active && member.Role == BoardRole.Admin) <= 1)
                return WorkOperation<BoardMemberRecord>.Failure("sole_board_admin");
        }

        var member = await store.UpsertBoardMemberAsync(
            boardId,
            targetUserId,
            role,
            clock.UtcNow,
            cancellationToken);

        await AuditAsync(
            resolved.Value.Board,
            actorUserId,
            "BOARD_MEMBER_UPDATED",
            correlationId,
            cancellationToken);

        return WorkOperation<BoardMemberRecord>.Success(member);
    }

    public async Task<WorkOperation<bool>> RemoveBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default, long? expectedMemberVersion = null)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null || !resolved.Value.Access.CanAdminister)
        {
            return WorkOperation<bool>.Failure("board_not_found");
        }

        var members = await store.ListBoardMembersAsync(
            boardId,
            cancellationToken);

        var target = members.FirstOrDefault(
            member => member.UserId == targetUserId && member.Active);

        if (expectedMemberVersion is not null && (expectedMemberVersion <= 0 || target?.Version != expectedMemberVersion))
            return WorkOperation<bool>.Failure("version_conflict");

        if (target is null)
        {
            return WorkOperation<bool>.Failure("member_not_found");
        }

        if (target.Role == BoardRole.Admin &&
            members.Count(member => member.Active && member.Role == BoardRole.Admin) <= 1 &&
            !IsOrganizationAdmin(resolved.Value.OrganizationMembership))
        {
            return WorkOperation<bool>.Failure("sole_board_admin");
        }

        if (!await store.RemoveBoardMemberAsync(
                boardId,
                targetUserId,
                clock.UtcNow,
                cancellationToken))
        {
            return WorkOperation<bool>.Failure("member_not_found");
        }

        await AuditAsync(
            resolved.Value.Board,
            actorUserId,
            "BOARD_MEMBER_REMOVED",
            correlationId,
            cancellationToken);

        return WorkOperation<bool>.Success(true);
    }

    public async Task<WorkOperation<BoardListRecord>> CreateListAsync(
        Guid boardId,
        Guid actorUserId,
        string name,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null ||
            !resolved.Value.Access.CanEdit ||
            resolved.Value.Board.LifecycleState != BoardLifecycleState.Active)
        {
            return WorkOperation<BoardListRecord>.Failure("board_not_found");
        }

        if (!TryNormalizeListName(name, out var normalizedName))
        {
            return WorkOperation<BoardListRecord>.Failure("invalid_list_name");
        }

        var normalizedRank = rank is null ? null : NormalizeRank(rank);
        if (rank is not null && normalizedRank is null)
        {
            return WorkOperation<BoardListRecord>.Failure("invalid_rank");
        }

        BoardListRecord list;
        try
        {
            list = await store.CreateListAsync(
                boardId,
                Guid.NewGuid(),
                normalizedName,
                normalizedRank,
                clock.UtcNow,
                cancellationToken);
        }
        catch (RankSpaceExhaustedException)
        {
            return WorkOperation<BoardListRecord>.Failure("rank_space_exhausted");
        }

        await RecordChangeAsync(list.OrganizationId, list.BoardId, actorUserId, "LIST_CREATED", "List", list.Id, list.Version, correlationId, cancellationToken);

        return WorkOperation<BoardListRecord>.Success(list);
    }

    public async Task<WorkOperation<BoardListRecord>> UpdateListAsync(
        Guid listId,
        Guid actorUserId,
        string name,
        string? rank,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default, Guid? beforeListId = null, bool moveToEnd = false)
    {
        var list = await store.FindListAsync(listId, cancellationToken);
        if (list is null)
        {
            return WorkOperation<BoardListRecord>.Failure("list_not_found");
        }

        var resolved = await ResolveAccessAsync(
            list.BoardId,
            actorUserId,
            cancellationToken);

        if (resolved is null ||
            !resolved.Value.Access.CanMove ||
            resolved.Value.Board.LifecycleState != BoardLifecycleState.Active ||
            list.LifecycleState != WorkItemLifecycleState.Active)
        {
            return WorkOperation<BoardListRecord>.Failure("list_not_found");
        }

        if (!TryNormalizeListName(name, out var normalizedName))
        {
            return WorkOperation<BoardListRecord>.Failure("invalid_list_name");
        }

        if (list.Version != expectedVersion) return WorkOperation<BoardListRecord>.Failure("version_conflict");
        if (beforeListId == Guid.Empty || beforeListId == listId || (beforeListId is not null && moveToEnd)
            || (rank is not null && (beforeListId is not null || moveToEnd)))
            return WorkOperation<BoardListRecord>.Failure("invalid_move_position");
        var normalizedRank = rank is null ? list.Rank : NormalizeRank(rank);
        if (normalizedRank is null)
        {
            return WorkOperation<BoardListRecord>.Failure("invalid_rank");
        }

        BoardListRecord? updated;
        try { updated = await store.UpdateListAsync(
            listId,
            normalizedName,
            normalizedRank,
            expectedVersion,
            clock.UtcNow,
            cancellationToken, beforeListId, moveToEnd); }
        catch (RankSpaceExhaustedException) { return WorkOperation<BoardListRecord>.Failure("rank_space_exhausted"); }

        if (updated is null)
        {
            return WorkOperation<BoardListRecord>.Failure("version_conflict");
        }

        await RecordChangeAsync(updated.OrganizationId, updated.BoardId, actorUserId, rank is null && beforeListId is null && !moveToEnd ? "LIST_RENAMED" : "LIST_MOVED", "List", updated.Id, updated.Version, correlationId, cancellationToken);

        return WorkOperation<BoardListRecord>.Success(updated);
    }

    public async Task<WorkOperation<BoardListRecord>> SetListLifecycleAsync(
        Guid listId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default, bool deletionConfirmed = false, long? expectedContainedCardCount = null)
    {
        var list = await store.FindListAsync(listId, cancellationToken);
        if (list is null)
        {
            return WorkOperation<BoardListRecord>.Failure("list_not_found");
        }

        var resolved = await ResolveAccessAsync(
            list.BoardId,
            actorUserId,
            cancellationToken);

        if (resolved is null || !resolved.Value.Access.CanAdminister ||
            resolved.Value.Board.LifecycleState != BoardLifecycleState.Active)
        {
            return WorkOperation<BoardListRecord>.Failure("list_not_found");
        }

        if (!IsValidLifecycleTransition(list.LifecycleState, nextState))
        {
            return WorkOperation<BoardListRecord>.Failure(
                "invalid_lifecycle_transition");
        }

        if (list.Version != expectedVersion) return WorkOperation<BoardListRecord>.Failure("version_conflict");
        if (nextState == WorkItemLifecycleState.Deleted)
        {
            if (!deletionConfirmed) return WorkOperation<BoardListRecord>.Failure("delete_confirmation_required");
            if (expectedContainedCardCount is null or < 0) return WorkOperation<BoardListRecord>.Failure("deletion_impact_required");
            // The owning command holds the Board gate, so card moves/lifecycle
            // commands cannot change this impact between review and deletion.
            if (await store.CountContainedCardsAsync(listId, cancellationToken) != expectedContainedCardCount)
                return WorkOperation<BoardListRecord>.Failure("deletion_impact_changed");
        }

        var updated = await store.SetListLifecycleAsync(
            listId,
            list.LifecycleState,
            nextState,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return WorkOperation<BoardListRecord>.Failure("version_conflict");
        }

        await RecordChangeAsync(updated.OrganizationId, updated.BoardId, actorUserId, EventForLifecycle("LIST", nextState), "List", updated.Id, updated.Version, correlationId, cancellationToken);

        return WorkOperation<BoardListRecord>.Success(updated);
    }

    public async Task<WorkOperation<CardRecord>> CreateCardAsync(
        Guid listId,
        Guid actorUserId,
        string title,
        string? description,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = await store.FindListAsync(listId, cancellationToken);
        if (list is null || list.LifecycleState != WorkItemLifecycleState.Active)
        {
            return WorkOperation<CardRecord>.Failure("list_not_found");
        }

        var resolved = await ResolveAccessAsync(
            list.BoardId,
            actorUserId,
            cancellationToken);

        if (resolved is null ||
            !resolved.Value.Access.CanEdit ||
            resolved.Value.Board.LifecycleState != BoardLifecycleState.Active)
        {
            return WorkOperation<CardRecord>.Failure("list_not_found");
        }

        if (!TryNormalizeCardTitle(title, out var normalizedTitle))
        {
            return WorkOperation<CardRecord>.Failure("invalid_card_title");
        }

        var normalizedRank = rank is null ? null : NormalizeRank(rank);
        if (rank is not null && normalizedRank is null)
        {
            return WorkOperation<CardRecord>.Failure("invalid_rank");
        }

        CardRecord card;
        try
        {
            card = await store.CreateCardAsync(
                listId,
                Guid.NewGuid(),
                normalizedTitle,
                NormalizeOptional(description),
                normalizedRank,
                clock.UtcNow,
                cancellationToken);
        }
        catch (RankSpaceExhaustedException)
        {
            return WorkOperation<CardRecord>.Failure("rank_space_exhausted");
        }

        await RecordChangeAsync(card.OrganizationId, card.BoardId, actorUserId, "CARD_CREATED", "Card", card.Id, card.Version, correlationId, cancellationToken);

        return WorkOperation<CardRecord>.Success(card);
    }

    public async Task<WorkOperation<CardRecord>> UpdateCardAsync(
        Guid cardId,
        Guid actorUserId,
        string title,
        string? description,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        if (card is null)
        {
            return WorkOperation<CardRecord>.Failure("card_not_found");
        }

        var parent = await store.FindListAsync(card.ListId, cancellationToken);
        if (parent is null || parent.LifecycleState != WorkItemLifecycleState.Active)
            return WorkOperation<CardRecord>.Failure("card_not_found");

        var resolved = await ResolveAccessAsync(
            card.BoardId,
            actorUserId,
            cancellationToken);

        if (resolved is null ||
            !resolved.Value.Access.CanEdit ||
            card.LifecycleState != WorkItemLifecycleState.Active)
        {
            return WorkOperation<CardRecord>.Failure("card_not_found");
        }

        if (!TryNormalizeCardTitle(title, out var normalizedTitle))
        {
            return WorkOperation<CardRecord>.Failure("invalid_card_title");
        }

        var updated = await store.UpdateCardAsync(
            cardId,
            normalizedTitle,
            NormalizeOptional(description),
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return WorkOperation<CardRecord>.Failure("version_conflict");
        }

        await RecordChangeAsync(updated.OrganizationId, updated.BoardId, actorUserId, "CARD_UPDATED", "Card", updated.Id, updated.Version, correlationId, cancellationToken);

        return WorkOperation<CardRecord>.Success(updated);
    }

    public async Task<WorkOperation<CardRecord>> MoveCardAsync(
        Guid cardId,
        Guid actorUserId,
        Guid destinationListId,
        string? rank,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default, Guid? beforeCardId = null)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        var source = card is null ? null : await store.FindListAsync(card.ListId, cancellationToken);
        var destination = await store.FindListAsync(
            destinationListId,
            cancellationToken);

        if (card is null ||
            source is null || source.LifecycleState != WorkItemLifecycleState.Active ||
            destination is null ||
            destination.BoardId != card.BoardId ||
            card.LifecycleState != WorkItemLifecycleState.Active ||
            destination.LifecycleState != WorkItemLifecycleState.Active ||
            (rank is not null && !RankToken.IsValid(rank)))
        {
            return WorkOperation<CardRecord>.Failure("card_not_found");
        }

        var resolved = await ResolveAccessAsync(
            card.BoardId,
            actorUserId,
            cancellationToken);

        if (resolved is null ||
            !resolved.Value.Access.CanMove ||
            resolved.Value.Board.LifecycleState != BoardLifecycleState.Active)
        {
            return WorkOperation<CardRecord>.Failure("card_not_found");
        }

        // Check only after admission; the owning Board command lock keeps the
        // production snapshot stable and avoids allocating for a known stale move.
        if (card.Version != expectedVersion)
            return WorkOperation<CardRecord>.Failure("version_conflict");
        if (beforeCardId is not null && (beforeCardId == Guid.Empty || beforeCardId == cardId || rank is not null))
            return WorkOperation<CardRecord>.Failure("invalid_move_position");

        CardRecord? updated;
        try
        {
            updated = await store.MoveCardAsync(cardId, destinationListId, rank, expectedVersion,
                clock.UtcNow, cancellationToken, beforeCardId);
        }
        catch (RankSpaceExhaustedException)
        {
            return WorkOperation<CardRecord>.Failure("rank_space_exhausted");
        }

        if (updated is null)
        {
            return WorkOperation<CardRecord>.Failure("version_conflict");
        }

        await RecordChangeAsync(updated.OrganizationId, updated.BoardId, actorUserId, "CARD_MOVED", "Card", updated.Id, updated.Version, correlationId, cancellationToken);

        return WorkOperation<CardRecord>.Success(updated);
    }

    public async Task<WorkOperation<CardRecord>> SetCardLifecycleAsync(
        Guid cardId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        if (card is null)
        {
            return WorkOperation<CardRecord>.Failure("card_not_found");
        }

        var parent = await store.FindListAsync(card.ListId, cancellationToken);
        if (parent is null || parent.LifecycleState == WorkItemLifecycleState.Deleted ||
            nextState != WorkItemLifecycleState.Deleted && parent.LifecycleState != WorkItemLifecycleState.Active)
            return WorkOperation<CardRecord>.Failure("card_not_found");

        var resolved = await ResolveAccessAsync(
            card.BoardId,
            actorUserId,
            cancellationToken);

        if (resolved is null || !resolved.Value.Access.CanEdit ||
            nextState == WorkItemLifecycleState.Deleted && !resolved.Value.Access.CanAdminister)
        {
            return WorkOperation<CardRecord>.Failure("card_not_found");
        }

        if (!IsValidLifecycleTransition(card.LifecycleState, nextState))
        {
            return WorkOperation<CardRecord>.Failure(
                "invalid_lifecycle_transition");
        }

        var updated = await store.SetCardLifecycleAsync(
            cardId,
            card.LifecycleState,
            nextState,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return WorkOperation<CardRecord>.Failure("version_conflict");
        }

        await RecordChangeAsync(updated.OrganizationId, updated.BoardId, actorUserId, EventForLifecycle("CARD", nextState), "Card", updated.Id, updated.Version, correlationId, cancellationToken);

        return WorkOperation<CardRecord>.Success(updated);
    }

    private async Task<(
        BoardRecord Board,
        OrganizationMembership? OrganizationMembership,
        BoardMemberRecord? BoardMember,
        BoardAccess Access)?> ResolveAccessAsync(
        Guid boardId,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var board = await store.FindBoardAsync(boardId, cancellationToken);
        if (board is null || board.LifecycleState == BoardLifecycleState.Deleted)
        {
            return null;
        }

        var organization = await organizationStore.FindOrganizationAsync(board.OrganizationId, cancellationToken);
        if (organization is null || organization.Status == OrganizationStatus.Deleting)
        {
            return null;
        }
        var organizationActive = organization.Status == OrganizationStatus.Active;

        OrganizationMembership? organizationMembership = null;
        BoardMemberRecord? boardMember = null;

        if (actorUserId.HasValue)
        {
            organizationMembership =
                await organizationStore.FindMembershipAsync(
                    board.OrganizationId,
                    actorUserId.Value,
                    cancellationToken);

            boardMember = await store.FindBoardMemberAsync(
                boardId,
                actorUserId.Value,
                cancellationToken);
        }

        var orgMember = organizationMembership is { Active: true };
        var orgAdmin = IsOrganizationAdmin(organizationMembership);
        // Board membership currently requires active Organization membership at
        // creation. Revocation must also invalidate persisted board grants at use.
        var explicitBoardMember = orgMember && boardMember is { Active: true };
        var boardAdmin = orgMember && boardMember is
        {
            Active: true,
            Role: BoardRole.Admin,
        };

        var active = board.LifecycleState == BoardLifecycleState.Active;
        var canView = board.Visibility switch
        {
            BoardVisibility.Public => active || orgMember || explicitBoardMember || orgAdmin,
            BoardVisibility.Organization => orgMember || explicitBoardMember || orgAdmin,
            BoardVisibility.Private => explicitBoardMember || orgAdmin,
            _ => false,
        };

        var canEdit =
            organizationActive &&
            active &&
            (orgAdmin || explicitBoardMember);

        var canAdminister = organizationActive && (orgAdmin || boardAdmin);

        return (
            board,
            organizationMembership,
            boardMember,
            new BoardAccess(
                canView,
                canEdit,
                canAdminister,
                canEdit));
    }

    // Retry authorization reads only permissions, not a potentially large board snapshot.
    internal async Task<bool> CheckCommandAccessAsync(Guid boardId, Guid actorId, string permission, CancellationToken cancellationToken)
    {
        var current = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        if (current is null) return false;
        return permission switch
        {
            "admin" => current.Value.Access.CanAdminister,
            "view" => current.Value.Access.CanView,
            _ => current.Value.Access.CanEdit,
        };
    }

    private async Task<WorkOperation<BoardRecord>> ChangeBoardLifecycleAsync(
        Guid boardId,
        Guid actorUserId,
        BoardLifecycleState expectedState,
        BoardLifecycleState nextState,
        long expectedVersion,
        string eventType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAccessAsync(
            boardId,
            actorUserId,
            cancellationToken);

        if (resolved is null ||
            !resolved.Value.Access.CanAdminister ||
            resolved.Value.Board.LifecycleState != expectedState)
        {
            return WorkOperation<BoardRecord>.Failure("board_not_found");
        }

        var updated = await store.SetBoardLifecycleAsync(
            boardId,
            expectedState,
            nextState,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return WorkOperation<BoardRecord>.Failure("version_conflict");
        }

        await AuditAsync(
            updated,
            actorUserId,
            eventType,
            correlationId,
            cancellationToken);

        return WorkOperation<BoardRecord>.Success(updated);
    }

    private Task AuditAsync(BoardRecord board, Guid actorUserId, string eventType,
        string correlationId, CancellationToken cancellationToken) =>
        RecordChangeAsync(board.OrganizationId, board.Id, actorUserId, eventType, "Board", board.Id, board.Version, correlationId, cancellationToken);

    private async Task RecordChangeAsync(Guid organizationId, Guid boardId, Guid actorUserId,
        string eventType, string entityType, Guid entityId, long version, string correlationId,
        CancellationToken cancellationToken)
    {
        await store.AppendAuditAsync(organizationId, actorUserId, eventType, entityType, entityId, correlationId, cancellationToken);
        await events.AppendAsync(new WorkEvent(Guid.NewGuid(), organizationId, boardId, actorUserId,
            eventType, entityType, entityId, version, correlationId, clock.UtcNow), cancellationToken);
    }

    private static bool IsOrganizationAdmin(
        OrganizationMembership? membership) =>
        membership is
        {
            Active: true,
            Role: OrganizationRole.Owner or OrganizationRole.Admin,
        };

    private static bool TryNormalizeName(
        string value,
        out string normalized) =>
        TryNormalizeRequired(value, 160, out normalized);

    private static bool TryNormalizeListName(
        string value,
        out string normalized) =>
        TryNormalizeRequired(value, 160, out normalized);

    private static bool TryNormalizeCardTitle(
        string value,
        out string normalized) =>
        TryNormalizeRequired(value, 500, out normalized);

    private static bool TryNormalizeRequired(
        string value,
        int maximumLength,
        out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 &&
            normalized.Length <= maximumLength;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRank(string? rank)
    {
        if (string.IsNullOrWhiteSpace(rank))
        {
            return RankToken.Initial();
        }

        return RankToken.IsValid(rank) ? rank : null;
    }

    private static bool TryNormalizeBackground(
        string? backgroundType,
        string? backgroundValue,
        out string normalizedType,
        out string? normalizedValue)
    {
        normalizedType = (backgroundType ?? "COLOR")
            .Trim()
            .ToUpperInvariant();
        normalizedValue = NormalizeOptional(backgroundValue);

        return normalizedType switch
        {
            "COLOR" => normalizedValue is null ||
                normalizedValue.Length <= 64,
            "IMAGE" => normalizedValue is not null &&
                normalizedValue.Length <= 2048,
            _ => false,
        };
    }

    private static bool IsValidLifecycleTransition(
        WorkItemLifecycleState current,
        WorkItemLifecycleState next) =>
        (current, next) is
            (WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived)
            or (WorkItemLifecycleState.Archived, WorkItemLifecycleState.Active)
            or (WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted);

    private static string EventForLifecycle(
        string entity,
        WorkItemLifecycleState next) =>
        next switch
        {
            WorkItemLifecycleState.Active => $"{entity}_RESTORED",
            WorkItemLifecycleState.Archived => $"{entity}_ARCHIVED",
            WorkItemLifecycleState.Deleted => $"{entity}_DELETED",
            _ => $"{entity}_UPDATED",
        };
}
