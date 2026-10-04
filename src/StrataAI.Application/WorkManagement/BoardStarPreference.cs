using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record BoardStarPreference(Guid OrganizationId, Guid BoardId, Guid UserId, bool Starred);

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<BoardStarPreference>> GetStarAsync(Guid boardId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        if (scope is null || !scope.Value.Access.CanView ||
            await organizationStore.FindOrganizationAsync(scope.Value.Board.OrganizationId, cancellationToken) is not { Status: OrganizationStatus.Active })
            return WorkOperation<BoardStarPreference>.Failure("board_not_found");
        return WorkOperation<BoardStarPreference>.Success(new(scope.Value.Board.OrganizationId, boardId, actorId,
            await store.GetStarAsync(boardId, actorId, cancellationToken)));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<BoardStarPreference>> GetStarAsync(Guid boardId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var board = await store.FindBoardAsync(boardId, cancellationToken);
        if (board is null) return WorkOperation<BoardStarPreference>.Failure("board_not_found");
        return await transactions.ExecuteReadAsync(board.OrganizationId, actorId, "board_not_found", async () =>
            await store.AcquireBoardReadScopeAsync(board.OrganizationId, actorId, boardId, cancellationToken) &&
            await organizations.FindOrganizationAsync(board.OrganizationId, cancellationToken) is { Status: OrganizationStatus.Active } &&
            await inner.CheckCommandAccessAsync(boardId, actorId, "view", cancellationToken),
            () => inner.GetStarAsync(boardId, actorId, cancellationToken), cancellationToken);
    }
}
