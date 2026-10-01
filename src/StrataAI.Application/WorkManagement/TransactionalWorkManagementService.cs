using StrataAI.Application.Organizations;
using StrataAI.Application.Identity;

namespace StrataAI.Application.WorkManagement;

// ARCH-03 / PRD-04..09: one commit includes the mutation and its audit.
public sealed class TransactionalWorkManagementService(
    WorkManagementService inner, IWorkManagementStore store,
    IWorkManagementUnitOfWork transactions, IOrganizationStore organizations, IWorkCommandContext context,
    ICommandActorAuthorization actors) : IWorkManagementService, IWorkBoardAuthorization
{
    public Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid boardId, Guid? actorId,
        CancellationToken cancellationToken = default) => inner.GetSyncScopeAsync(boardId, actorId, cancellationToken);

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
        transactions.ExecuteAsync(organizationId, WorkCommand.Create(actorUserId, context.IdempotencyKey, "CreateBoardAsync", organizationId, new { name, description, visibility, backgroundType, backgroundValue }, "organization_not_found"), value => value is null ? AuthorizeOrganization(organizationId, actorUserId, cancellationToken) : AuthorizeBoard(value.Id, actorUserId, "view", cancellationToken), () => inner.CreateBoardAsync(organizationId, actorUserId, name, description, visibility, backgroundType, backgroundValue, correlationId, cancellationToken), cancellationToken);

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
        BoardCommand(boardId, actorUserId, "edit", WorkCommand.Create(actorUserId, context.IdempotencyKey, "UpdateBoardAsync", boardId, new { name, description, backgroundType, backgroundValue, expectedVersion }, "board_not_found"), () => inner.UpdateBoardAsync(boardId, actorUserId, name, description, backgroundType, backgroundValue, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> SetBoardVisibilityAsync(
        Guid boardId,
        Guid actorUserId,
        BoardVisibility visibility,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "admin", WorkCommand.Create(actorUserId, context.IdempotencyKey, "SetBoardVisibilityAsync", boardId, new { visibility, expectedVersion }, "board_not_found"), () => inner.SetBoardVisibilityAsync(boardId, actorUserId, visibility, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> ArchiveBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "admin", WorkCommand.Create(actorUserId, context.IdempotencyKey, "ArchiveBoardAsync", boardId, new { expectedVersion }, "board_not_found"), () => inner.ArchiveBoardAsync(boardId, actorUserId, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> RestoreBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "admin", WorkCommand.Create(actorUserId, context.IdempotencyKey, "RestoreBoardAsync", boardId, new { expectedVersion }, "board_not_found"), () => inner.RestoreBoardAsync(boardId, actorUserId, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardRecord>> DeleteBoardAsync(
        Guid boardId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "admin", WorkCommand.Create(actorUserId, context.IdempotencyKey, "DeleteBoardAsync", boardId, new { expectedVersion }, "board_not_found"), () => inner.DeleteBoardAsync(boardId, actorUserId, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<bool>> SetStarAsync(
        Guid boardId,
        Guid actorUserId,
        bool starred,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "view", WorkCommand.Create(actorUserId, context.IdempotencyKey, "SetStarAsync", boardId, new { starred }, "board_not_found"), () => inner.SetStarAsync(boardId, actorUserId, starred, cancellationToken), cancellationToken);

    public Task<WorkOperation<IReadOnlyList<BoardMemberRecord>>> ListBoardMembersAsync(
        Guid boardId,
        Guid actorUserId,
        CancellationToken cancellationToken = default, Guid? after = null) =>
        BoardCommand(boardId, actorUserId, "admin",
            WorkCommand.Create(actorUserId, null, "ListBoardMembersAsync", boardId, new { }, "board_not_found"), async () =>
            {
                var result = await inner.ListBoardMembersAsync(boardId, actorUserId, cancellationToken, after);
                if (!result.Succeeded) return result;
                if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                    return WorkOperation<IReadOnlyList<BoardMemberRecord>>.Failure("session_unavailable");
                return result;
            }, cancellationToken);

    public Task<WorkOperation<BoardMemberRecord>> SetBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        BoardRole role,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "admin", WorkCommand.Create(actorUserId, context.IdempotencyKey, "SetBoardMemberAsync", boardId, new { targetUserId, role }, "board_not_found"), () => inner.SetBoardMemberAsync(boardId, actorUserId, targetUserId, role, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<bool>> RemoveBoardMemberAsync(
        Guid boardId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "admin", WorkCommand.Create(actorUserId, context.IdempotencyKey, "RemoveBoardMemberAsync", boardId, new { targetUserId }, "board_not_found"), () => inner.RemoveBoardMemberAsync(boardId, actorUserId, targetUserId, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardListRecord>> CreateListAsync(
        Guid boardId,
        Guid actorUserId,
        string name,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorUserId, "edit", WorkCommand.Create(actorUserId, context.IdempotencyKey, "CreateListAsync", boardId, new { name, rank }, "board_not_found"), () => inner.CreateListAsync(boardId, actorUserId, name, rank, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardListRecord>> UpdateListAsync(
        Guid listId,
        Guid actorUserId,
        string name,
        string? rank,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ListCommand(listId, actorUserId, "edit", WorkCommand.Create(actorUserId, context.IdempotencyKey, "UpdateListAsync", listId, new { name, rank, expectedVersion }, "list_not_found"), () => inner.UpdateListAsync(listId, actorUserId, name, rank, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<BoardListRecord>> SetListLifecycleAsync(
        Guid listId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ListCommand(listId, actorUserId, "admin", WorkCommand.Create(actorUserId, context.IdempotencyKey, "SetListLifecycleAsync", listId, new { nextState, expectedVersion }, "list_not_found"), () => inner.SetListLifecycleAsync(listId, actorUserId, nextState, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<CardRecord>> CreateCardAsync(
        Guid listId,
        Guid actorUserId,
        string title,
        string? description,
        string? rank,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ListCommand(listId, actorUserId, "edit", WorkCommand.Create(actorUserId, context.IdempotencyKey, "CreateCardAsync", listId, new { title, description, rank }, "list_not_found"), () => inner.CreateCardAsync(listId, actorUserId, title, description, rank, correlationId, cancellationToken), cancellationToken);

    public Task<WorkOperation<CardRecord>> UpdateCardAsync(
        Guid cardId,
        Guid actorUserId,
        string title,
        string? description,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        CardCommand(cardId, actorUserId, "edit", WorkCommand.Create(actorUserId, context.IdempotencyKey, "UpdateCardAsync", cardId, new { title, description, expectedVersion }, "card_not_found"), () => inner.UpdateCardAsync(cardId, actorUserId, title, description, expectedVersion, correlationId, cancellationToken), cancellationToken);

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
        return await transactions.ExecuteAsync(card.OrganizationId, WorkCommand.Create(actorUserId, context.IdempotencyKey, "MoveCardAsync", cardId, new { destinationListId, rank, expectedVersion }, "card_not_found"), _ => AuthorizeBoard(card.BoardId, actorUserId, "edit", cancellationToken), () => inner.MoveCardAsync(cardId, actorUserId, destinationListId, rank, expectedVersion, correlationId, cancellationToken), cancellationToken);
    }

    public Task<WorkOperation<CardRecord>> SetCardLifecycleAsync(
        Guid cardId,
        Guid actorUserId,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        CardCommand(cardId, actorUserId, nextState == WorkItemLifecycleState.Deleted ? "admin" : "edit", WorkCommand.Create(actorUserId, context.IdempotencyKey, "SetCardLifecycleAsync", cardId, new { nextState, expectedVersion }, "card_not_found"), () => inner.SetCardLifecycleAsync(cardId, actorUserId, nextState, expectedVersion, correlationId, cancellationToken), cancellationToken);

    private async Task<WorkOperation<T>> BoardCommand<T>(Guid id, Guid actorId, string permission, WorkCommand command, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken)
    {
        var resource = await store.FindBoardAsync(id, cancellationToken);
        return resource is null ? WorkOperation<T>.Failure("board_not_found") :
            await transactions.ExecuteAsync(resource.OrganizationId, command, _ => AuthorizeBoard(resource.Id, actorId, permission, cancellationToken), operation, cancellationToken);
    }

    private async Task<WorkOperation<T>> ListCommand<T>(Guid id, Guid actorId, string permission, WorkCommand command, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken)
    {
        var resource = await store.FindListAsync(id, cancellationToken);
        return resource is null ? WorkOperation<T>.Failure("list_not_found") :
            await transactions.ExecuteAsync(resource.OrganizationId, command, _ => AuthorizeBoard(resource.BoardId, actorId, permission, cancellationToken), operation, cancellationToken);
    }

    private async Task<WorkOperation<T>> CardCommand<T>(Guid id, Guid actorId, string permission, WorkCommand command, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken)
    {
        var resource = await store.FindCardAsync(id, cancellationToken);
        return resource is null ? WorkOperation<T>.Failure("card_not_found") :
            await transactions.ExecuteAsync(resource.OrganizationId, command, _ => AuthorizeBoard(resource.BoardId, actorId, permission, cancellationToken), operation, cancellationToken);
    }

    private async Task<bool> AuthorizeOrganization(Guid organizationId, Guid actorId, CancellationToken cancellationToken) =>
        await store.AcquireCommandScopeAsync(organizationId, actorId, null, cancellationToken) &&
        await organizations.FindOrganizationAsync(organizationId, cancellationToken) is { Status: OrganizationStatus.Active } &&
        await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken) is { Active: true };

    private async Task<bool> AuthorizeBoard(Guid boardId, Guid actorId, string permission, CancellationToken cancellationToken)
    {
        var board = await store.FindBoardAsync(boardId, cancellationToken);
        return board is not null &&
            await store.AcquireCommandScopeAsync(board.OrganizationId, actorId, boardId, cancellationToken) &&
            await organizations.FindOrganizationAsync(board.OrganizationId, cancellationToken) is { Status: OrganizationStatus.Active } &&
            await inner.CheckCommandAccessAsync(boardId, actorId, permission, cancellationToken);
    }

}
