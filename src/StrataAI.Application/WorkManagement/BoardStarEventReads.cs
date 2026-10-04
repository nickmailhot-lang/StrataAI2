using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record BoardStarEventPage(Guid OrganizationId, Guid BoardId, Guid UserId,
    IReadOnlyList<BoardStarEvent> Items, long? NextAfter);

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<BoardStarEventPage>> GetStarEventsAsync(Guid boardId, Guid actorId, long after = 0,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        if (scope is null || !scope.Value.Access.CanView ||
            await organizationStore.FindOrganizationAsync(scope.Value.Board.OrganizationId, cancellationToken) is not { Status: OrganizationStatus.Active })
            return WorkOperation<BoardStarEventPage>.Failure("board_not_found");
        if (after < 0) return WorkOperation<BoardStarEventPage>.Failure("invalid_board_star_cursor");
        var rows = await store.ListStarEventsAsync(boardId, actorId, after, 51, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<BoardStarEventPage>.Success(new(scope.Value.Board.OrganizationId,boardId,actorId,items,
            rows.Count > 50 ? items[^1].Version : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<BoardStarEventPage>> GetStarEventsAsync(Guid boardId, Guid actorId, long after = 0,
        CancellationToken cancellationToken = default)
    {
        var board = await store.FindBoardAsync(boardId, cancellationToken);
        if (board is null) return WorkOperation<BoardStarEventPage>.Failure("board_not_found");
        return await transactions.ExecuteReadAsync(board.OrganizationId, actorId, "board_not_found", async () =>
            await store.AcquireBoardReadScopeAsync(board.OrganizationId,actorId,boardId,cancellationToken) &&
            await organizations.FindOrganizationAsync(board.OrganizationId,cancellationToken) is { Status: OrganizationStatus.Active } &&
            await inner.CheckCommandAccessAsync(boardId,actorId,"view",cancellationToken),
            () => inner.GetStarEventsAsync(boardId,actorId,after,cancellationToken), cancellationToken);
    }
}
