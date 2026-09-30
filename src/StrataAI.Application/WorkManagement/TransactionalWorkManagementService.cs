namespace StrataAI.Application.WorkManagement;

// ARCH-03 / PRD-04..09: one commit includes the mutation and its audit.
public sealed class TransactionalWorkManagementService(
    WorkManagementService inner, IWorkManagementStore store,
    IWorkManagementUnitOfWork transactions) : IWorkManagementService
{
    public Task<WorkOperation<BoardRecord>> CreateBoardAsync(
        Guid organizationId,
        Guid actorUserId,
        string name,
        string? description,
        BoardVisibility visibility,
        string? backgroundType,
        string? backgroundValue,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        organizationId == Guid.Empty ? Task.FromResult(WorkOperation<BoardRecord>.Failure("organization_not_found")) :
        transactions.ExecuteAsync(organizationId, () => inner.CreateBoardAsync(organizationId, actorUserId, name, description, visibility, backgroundType, backgroundValue, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardSnapshot>> GetBoardAsync(
        Guid boardId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default) =>
        inner.GetBoardAsync(boardId, actorUserId, cancellationToken);

    public Task<WorkOperation<BoardRecord>> UpdateBoardAsync(
        Guid boardId,
        Guid actorUserId,
        string name,
        string? description,
        string? backgroundType,
        string? backgroundValue,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.UpdateBoardAsync(boardId, actorUserId, name, description, backgroundType, backgroundValue, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> SetBoardVisibilityAsync(
        Guid boardId,
        Guid actorUserId,
        BoardVisibility visibility,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.SetBoardVisibilityAsync(boardId, actorUserId, visibility, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> ArchiveBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.ArchiveBoardAsync(boardId, actorUserId, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> RestoreBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.RestoreBoardAsync(boardId, actorUserId, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> DeleteBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.DeleteBoardAsync(boardId, actorUserId, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<bool>> SetStarAsync(
        Guid boardId,
        Guid actorUserId,
        bool starred,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.SetStarAsync(boardId, actorUserId, starred, cancellationToken), cancellationToken);

    public Task<WorkOperation<IReadOnlyList<BoardMemberRecord>>> ListBoardMembersAsync(
        Guid boardId,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        inner.ListBoardMembersAsync(boardId, actorUserId, cancellationToken);

    public Task<WorkOperation<BoardMemberRecord>> SetBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        BoardRole role,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.SetBoardMemberAsync(boardId, actorUserId, targetUserId, role, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<bool>> RemoveBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.RemoveBoardMemberAsync(boardId, actorUserId, targetUserId, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardListRecord>> CreateListAsync(
        Guid boardId,
        Guid actorUserId,
        string name,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, () => inner.CreateListAsync(boardId, actorUserId, name, rank, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardListRecord>> UpdateListAsync(
        Guid listId,
        Guid actorUserId,
        string name,
        string? rank,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ListCommand(listId, () => inner.UpdateListAsync(listId, actorUserId, name, rank, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardListRecord>> SetListLifecycleAsync(
        Guid listId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ListCommand(listId, () => inner.SetListLifecycleAsync(listId, actorUserId, nextState, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<CardRecord>> CreateCardAsync(
        Guid listId,
        Guid actorUserId,
        string title,
        string? description,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ListCommand(listId, () => inner.CreateCardAsync(listId, actorUserId, title, description, rank, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<CardRecord>> UpdateCardAsync(
        Guid cardId,
        Guid actorUserId,
        string title,
        string? description,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        CardCommand(cardId, () => inner.UpdateCardAsync(cardId, actorUserId, title, description, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public async Task<WorkOperation<CardRecord>> MoveCardAsync(
        Guid cardId,
        Guid actorUserId,
        Guid destinationListId,
        string rank,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        var destination = await store.FindListAsync(destinationListId, cancellationToken);
        if (card is null || destination is null || card.OrganizationId != destination.OrganizationId || card.BoardId != destination.BoardId)
            return WorkOperation<CardRecord>.Failure("card_not_found");
        return await transactions.ExecuteAsync(card.OrganizationId, () => inner.MoveCardAsync(cardId, actorUserId, destinationListId, rank, expectedVersion, correlationId, cancellationToken), cancellationToken);
    }

    public Task<WorkOperation<CardRecord>> SetCardLifecycleAsync(
        Guid cardId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        CardCommand(cardId, () => inner.SetCardLifecycleAsync(cardId, actorUserId, nextState, expectedVersion, correlationId, cancellationToken), cancellationToken);

    private async Task<WorkOperation<T>> BoardCommand<T>(Guid id, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken)
    {
        var resource = await store.FindBoardAsync(id, cancellationToken);
        return resource is null ? WorkOperation<T>.Failure("board_not_found") :
            await transactions.ExecuteAsync(resource.OrganizationId, operation, cancellationToken);
    }

    private async Task<WorkOperation<T>> ListCommand<T>(Guid id, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken)
    {
        var resource = await store.FindListAsync(id, cancellationToken);
        return resource is null ? WorkOperation<T>.Failure("list_not_found") :
            await transactions.ExecuteAsync(resource.OrganizationId, operation, cancellationToken);
    }

    private async Task<WorkOperation<T>> CardCommand<T>(Guid id, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken)
    {
        var resource = await store.FindCardAsync(id, cancellationToken);
        return resource is null ? WorkOperation<T>.Failure("card_not_found") :
            await transactions.ExecuteAsync(resource.OrganizationId, operation, cancellationToken);
    }

}
