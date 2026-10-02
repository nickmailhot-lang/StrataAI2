namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<CardLabelChange>> SetCardLabelAsync(Guid cardId, Guid labelId, Guid actorId, bool assigned,
        long version, string correlationId, CancellationToken cancellationToken = default)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        if (card is not { LifecycleState: WorkItemLifecycleState.Active }) return WorkOperation<CardLabelChange>.Failure("card_not_found");
        var parent = await store.FindListAsync(card.ListId, cancellationToken);
        var access = await ResolveAccessAsync(card.BoardId, actorId, cancellationToken);
        if (parent is not { LifecycleState: WorkItemLifecycleState.Active } || access is not { Access.CanEdit: true }
            || access.Value.Board.LifecycleState != BoardLifecycleState.Active) return WorkOperation<CardLabelChange>.Failure("card_not_found");
        var label = await store.FindLabelAsync(labelId, cancellationToken);
        if (label is null || label.OrganizationId != card.OrganizationId || label.BoardId != card.BoardId)
            return WorkOperation<CardLabelChange>.Failure("label_not_found");
        var change = await store.SetCardLabelAsync(cardId, labelId, assigned, version, clock.UtcNow, cancellationToken);
        if (change is null) return WorkOperation<CardLabelChange>.Failure("version_conflict");
        if (change.Changed)
            await RecordChangeAsync(card.OrganizationId, card.BoardId, actorId, assigned ? "LABEL_ADDED" : "LABEL_REMOVED",
                "Card", cardId, change.Card.Version, correlationId, cancellationToken);
        return WorkOperation<CardLabelChange>.Success(change);
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<CardLabelChange>> SetCardLabelAsync(Guid cardId, Guid labelId, Guid actorId, bool assigned,
        long version, string correlationId, CancellationToken cancellationToken = default)
    {
        var hint = await store.FindCardAsync(cardId, cancellationToken);
        if (hint is null) return WorkOperation<CardLabelChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actorId, context.IdempotencyKey, "SetCardLabelAsync", cardId, new { labelId, assigned, version }, "card_not_found"),
            async _ =>
            {
                if (!await AuthorizeBoard(hint.BoardId, actorId, "edit", cancellationToken)
                    || (await store.FindBoardAsync(hint.BoardId, cancellationToken))?.LifecycleState != BoardLifecycleState.Active) return false;
                var current = await store.FindCardAsync(cardId, cancellationToken);
                var label = await store.FindLabelAsync(labelId, cancellationToken);
                return current is { LifecycleState: WorkItemLifecycleState.Active }
                    && current.OrganizationId == hint.OrganizationId && current.BoardId == hint.BoardId && current.ListId == hint.ListId
                    && await store.FindListAsync(current.ListId, cancellationToken) is { LifecycleState: WorkItemLifecycleState.Active }
                    && label is not null && label.OrganizationId == current.OrganizationId && label.BoardId == current.BoardId;
            }, () => inner.SetCardLabelAsync(cardId, labelId, actorId, assigned, version, correlationId, cancellationToken), cancellationToken);
    }
}
