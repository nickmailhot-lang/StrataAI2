namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<CardLabelOptionsPage>> ListCardLabelOptionsAsync(Guid cardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        if (card is not { LifecycleState: WorkItemLifecycleState.Active }) return WorkOperation<CardLabelOptionsPage>.Failure("card_not_found");
        var access = await ResolveAccessAsync(card.BoardId, actorId, cancellationToken);
        if (access is not { Access.CanEdit: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active
            || await store.FindListAsync(card.ListId, cancellationToken) is not { LifecycleState: WorkItemLifecycleState.Active })
            return WorkOperation<CardLabelOptionsPage>.Failure("card_not_found");
        if (after == Guid.Empty) return WorkOperation<CardLabelOptionsPage>.Failure("invalid_label_cursor");
        var rows = await store.ListCardLabelOptionsAsync(cardId, after, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<CardLabelOptionsPage>.Success(new(card.OrganizationId, card.BoardId, cardId, card.Version,
            items, rows.Count > 50 ? items[^1].Label.Id : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<CardLabelOptionsPage>> ListCardLabelOptionsAsync(Guid cardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default)
    {
        var hint = await store.FindCardAsync(cardId, cancellationToken);
        if (hint is null) return WorkOperation<CardLabelOptionsPage>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actorId, null, "ListCardLabelOptionsAsync", cardId, new { }, "card_not_found"),
            async _ =>
            {
                if (!await AuthorizeBoard(hint.BoardId, actorId, "edit", cancellationToken)) return false;
                var current = await store.FindCardAsync(cardId, cancellationToken);
                return current is not null && current.OrganizationId == hint.OrganizationId && current.BoardId == hint.BoardId && current.ListId == hint.ListId;
            }, async () =>
            {
                var result = await inner.ListCardLabelOptionsAsync(cardId, actorId, after, cancellationToken);
                return result.Succeeded && !await actors.VerifyAsync(actorId, cancellationToken)
                    ? WorkOperation<CardLabelOptionsPage>.Failure("session_unavailable") : result;
            }, cancellationToken);
    }
}
