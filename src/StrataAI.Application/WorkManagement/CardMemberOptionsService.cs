namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<CardMemberOptionsPage>> ListCardMemberOptionsAsync(Guid cardId, Guid actorId, Guid? after = null,
        CancellationToken cancellationToken = default)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken);
        if (card is not { LifecycleState: WorkItemLifecycleState.Active }) return WorkOperation<CardMemberOptionsPage>.Failure("card_not_found");
        var access = await ResolveAccessAsync(card.BoardId, actorId, cancellationToken);
        if (access is not { Access.CanEdit: true, OrganizationMembership.Active: true }
            || access.Value.Board.LifecycleState != BoardLifecycleState.Active
            || await store.FindListAsync(card.ListId, cancellationToken) is not { LifecycleState: WorkItemLifecycleState.Active })
            return WorkOperation<CardMemberOptionsPage>.Failure("card_not_found");
        if (after == Guid.Empty) return WorkOperation<CardMemberOptionsPage>.Failure("invalid_board_member_cursor");
        var rows = await store.ListCardMemberOptionsAsync(cardId, after, identityPolicy.RequireVerifiedEmail, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<CardMemberOptionsPage>.Success(new(card.OrganizationId, card.BoardId, cardId, card.Version,
            items, rows.Count > 50 ? items[^1].UserId : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<CardMemberOptionsPage>> ListCardMemberOptionsAsync(Guid cardId, Guid actorId, Guid? after = null,
        CancellationToken cancellationToken = default)
    {
        var hint = await store.FindCardAsync(cardId, cancellationToken);
        if (hint is null) return WorkOperation<CardMemberOptionsPage>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actorId, null, "ListCardMemberOptionsAsync", cardId, new { }, "card_not_found"), async _ =>
            {
                if (!await AuthorizeBoard(hint.BoardId, actorId, "edit", cancellationToken)) return false;
                var current = await store.FindCardAsync(cardId, cancellationToken);
                return current is not null && current.OrganizationId == hint.OrganizationId && current.BoardId == hint.BoardId && current.ListId == hint.ListId;
            }, async () =>
            {
                var result = await inner.ListCardMemberOptionsAsync(cardId, actorId, after, cancellationToken);
                return result.Succeeded && !await actors.VerifyAsync(actorId, cancellationToken)
                    ? WorkOperation<CardMemberOptionsPage>.Failure("session_unavailable") : result;
            }, cancellationToken);
    }
}
