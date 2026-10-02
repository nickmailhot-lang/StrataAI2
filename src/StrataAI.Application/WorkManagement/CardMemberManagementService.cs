namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<CardMemberChange>> SetCardMemberAsync(Guid cardId, Guid userId, Guid actorId, bool assigned,
        long version, string correlationId, CancellationToken cancellationToken = default)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        if (card is not { LifecycleState: WorkItemLifecycleState.Active }) return WorkOperation<CardMemberChange>.Failure("card_not_found");
        var access = await ResolveAccessAsync(card.BoardId, actorId, cancellationToken);
        if (access is not { Access.CanEdit: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active
            || await store.FindListAsync(card.ListId, cancellationToken) is not { LifecycleState: WorkItemLifecycleState.Active })
            return WorkOperation<CardMemberChange>.Failure("card_not_found");
        if (version < 1) return WorkOperation<CardMemberChange>.Failure("invalid_card_member_version");
        if (userId == Guid.Empty || (assigned && !await store.IsAssignableBoardMemberAsync(card.BoardId, userId, identityPolicy.RequireVerifiedEmail, cancellationToken)))
            return WorkOperation<CardMemberChange>.Failure("card_not_found");
        var change = await store.SetCardMemberAsync(cardId, userId, actorId, assigned, version, clock.UtcNow, cancellationToken);
        if (change is null) return WorkOperation<CardMemberChange>.Failure("version_conflict");
        if (change.Changed)
            await RecordChangeAsync(card.OrganizationId, card.BoardId, actorId, assigned ? "CARD_MEMBER_ADDED" : "CARD_MEMBER_REMOVED",
                "Card", cardId, change.Card.Version, correlationId, cancellationToken);
        return WorkOperation<CardMemberChange>.Success(change);
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<CardMemberChange>> SetCardMemberAsync(Guid cardId, Guid userId, Guid actorId, bool assigned,
        long version, string correlationId, CancellationToken cancellationToken = default)
    {
        var hint = await store.FindCardAsync(cardId, cancellationToken);
        if (hint is null) return WorkOperation<CardMemberChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actorId, context.IdempotencyKey, "SetCardMemberAsync", cardId, new { userId, assigned, version }, "card_not_found"),
            async _ =>
            {
                if (!await AuthorizeBoard(hint.BoardId, actorId, "edit", cancellationToken)
                    || (await store.FindBoardAsync(hint.BoardId, cancellationToken))?.LifecycleState != BoardLifecycleState.Active) return false;
                var current = await store.FindCardAsync(cardId, cancellationToken);
                return current is { LifecycleState: WorkItemLifecycleState.Active } && userId != Guid.Empty
                    && current.OrganizationId == hint.OrganizationId && current.BoardId == hint.BoardId && current.ListId == hint.ListId
                    && await store.FindListAsync(current.ListId, cancellationToken) is { LifecycleState: WorkItemLifecycleState.Active }
                    && (!assigned || await store.IsAssignableBoardMemberAsync(current.BoardId, userId,
                        inner.RequiresVerifiedAssignmentEmail, cancellationToken));
            }, () => inner.SetCardMemberAsync(cardId, userId, actorId, assigned, version, correlationId, cancellationToken), cancellationToken);
    }
}
