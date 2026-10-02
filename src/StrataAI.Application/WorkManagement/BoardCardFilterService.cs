namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<BoardCardFilterPage>> FilterBoardCardsAsync(Guid boardId, Guid actorId, string? keyword,
        IReadOnlyList<Guid> labelIds, string? match, Guid? after = null, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        if (access is not { Access.CanView: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active)
            return WorkOperation<BoardCardFilterPage>.Failure("board_not_found");
        var text = (keyword ?? "").Trim(); var mode = (match ?? "all").ToLowerInvariant();
        if (text.Length > 160 || labelIds.Count > 25 || labelIds.Any(id => id == Guid.Empty)
            || labelIds.Distinct().Count() != labelIds.Count || mode is not ("all" or "any") || after == Guid.Empty)
            return WorkOperation<BoardCardFilterPage>.Failure("invalid_board_filter");
        var rows = await store.FilterBoardCardsAsync(boardId, new(text, labelIds, mode == "all"), after, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<BoardCardFilterPage>.Success(new(access.Value.Board.OrganizationId, boardId, items,
            rows.Count > 50 ? items[^1].Id : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public Task<WorkOperation<BoardCardFilterPage>> FilterBoardCardsAsync(Guid boardId, Guid actorId, string? keyword,
        IReadOnlyList<Guid> labelIds, string? match, Guid? after = null, CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorId, "view", WorkCommand.Create(actorId, null, "FilterBoardCardsAsync", boardId, new { }, "board_not_found"), async () =>
        {
            var result = await inner.FilterBoardCardsAsync(boardId, actorId, keyword, labelIds, match, after, cancellationToken);
            return result.Succeeded && !await actors.VerifyAsync(actorId, cancellationToken)
                ? WorkOperation<BoardCardFilterPage>.Failure("session_unavailable") : result;
        }, cancellationToken);
}
