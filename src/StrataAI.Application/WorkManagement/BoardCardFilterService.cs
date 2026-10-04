namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<BoardCardFilterPage>> FilterBoardCardsAsync(Guid boardId, Guid actorId, string? keyword,
        IReadOnlyList<Guid> labelIds, string? match, Guid? after = null, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? memberIds = null, string? completion = null, string? due = null, string? activity = null)
    {
        var access = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        var members = memberIds ?? [];
        if (access is not { Access.CanView: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active
            || (members.Count > 0 && access.Value.OrganizationMembership is not { Active: true }))
            return WorkOperation<BoardCardFilterPage>.Failure("board_not_found");
        var text = (keyword ?? "").Trim(); var mode = (match ?? "all").ToLowerInvariant();
        var completionMode = (completion ?? "all").ToLowerInvariant();
        var dueMode = (due ?? "all").ToLowerInvariant();
        var activityMode = (activity ?? "all").ToLowerInvariant();
        if (text.Length > 160 || labelIds.Count > 25 || labelIds.Any(id => id == Guid.Empty)
            || labelIds.Distinct().Count() != labelIds.Count || members.Count > 25 || members.Any(id => id == Guid.Empty)
            || members.Distinct().Count() != members.Count || mode is not ("all" or "any") || after == Guid.Empty
            || completionMode is not ("all" or "complete" or "incomplete") || dueMode is not ("all" or "none" or "overdue" or "upcoming")
            || activityMode is not ("all" or "day" or "week" or "month"))
            return WorkOperation<BoardCardFilterPage>.Failure("invalid_board_filter");
        var observed = clock.UtcNow;
        var evaluatedAt = new DateTimeOffset(observed.UtcTicks - observed.UtcTicks % 10, TimeSpan.Zero);
        DateTimeOffset? updatedSince = activityMode == "all" ? null : evaluatedAt.AddDays(activityMode switch { "day" => -1, "week" => -7, _ => -30 });
        var rows = await store.FilterBoardCardsAsync(boardId, new(text, labelIds, mode == "all", members, identityPolicy.RequireVerifiedEmail,
            completionMode == "all" ? null : completionMode == "complete", dueMode, evaluatedAt, updatedSince), after, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<BoardCardFilterPage>.Success(new(access.Value.Board.OrganizationId, boardId, items,
            rows.Count > 50 ? items[^1].Id : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public Task<WorkOperation<BoardCardFilterPage>> FilterBoardCardsAsync(Guid boardId, Guid actorId, string? keyword,
        IReadOnlyList<Guid> labelIds, string? match, Guid? after = null, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? memberIds = null, string? completion = null, string? due = null, string? activity = null) =>
        BoardCommand(boardId, actorId, "view", WorkCommand.Create(actorId, null, "FilterBoardCardsAsync", boardId, new { }, "board_not_found"), async () =>
        {
            var result = await inner.FilterBoardCardsAsync(boardId, actorId, keyword, labelIds, match, after, cancellationToken, memberIds, completion, due, activity);
            return result.Succeeded && !await actors.VerifyAsync(actorId, cancellationToken)
                ? WorkOperation<BoardCardFilterPage>.Failure("session_unavailable") : result;
        }, cancellationToken);
}
