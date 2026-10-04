namespace StrataAI.Application.WorkManagement;

public sealed record SearchLabelContext(Guid Id, string Name, string Color);
public sealed record SearchMemberContext(Guid UserId, string DisplayName);
// Source kind is explicit so later comment projections can join the search
// response without treating comment snippets as canonical Card descriptions.
public sealed record SearchCardDocument(string SourceKind, CardRecord Card, string BoardName, string ListName,
    IReadOnlyList<SearchLabelContext> Labels, bool HasMoreLabels,
    IReadOnlyList<SearchMemberContext> Members, bool HasMoreMembers);
public sealed record BoardSearchPage(Guid OrganizationId, Guid BoardId, IReadOnlyList<SearchCardDocument> Items, Guid? NextCard);

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<BoardSearchPage>> SearchBoardAsync(Guid boardId, GlobalSearchBinding binding,
        Guid? after = null, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(boardId, binding.ActorId, cancellationToken);
        if (access is not { Access.CanView: true } || access.Value.OrganizationMembership is not { Active: true })
            return WorkOperation<BoardSearchPage>.Failure("board_not_found");
        static bool Text(string? value) => value is not null && value.Length <= 160 && value == value.Trim();
        if (binding.ActorId == Guid.Empty || !Text(binding.Keyword) || !Text(binding.Label) || !Text(binding.Member)
            || !Enum.IsDefined(binding.Scope) || after == Guid.Empty)
            return WorkOperation<BoardSearchPage>.Failure("invalid_search");
        var rows = await store.SearchBoardCardsAsync(boardId, binding, identityPolicy.RequireVerifiedEmail, after, cancellationToken);
        var documents = new List<SearchCardDocument>();
        foreach (var card in rows.Take(50))
        {
            var list = await store.FindListAsync(card.ListId, cancellationToken);
            if (list is null || list.OrganizationId != card.OrganizationId || list.BoardId != boardId
                || list.LifecycleState == WorkItemLifecycleState.Deleted)
                return WorkOperation<BoardSearchPage>.Failure("board_not_found");
            var labels = await store.ListCardLabelsAsync(card.Id, null, cancellationToken);
            var members = await store.ListCardMembersAsync(card.Id, null, identityPolicy.RequireVerifiedEmail, cancellationToken);
            documents.Add(new("CARD", card, access.Value.Board.Name, list.Name,
                labels.Take(50).Select(l => new SearchLabelContext(l.Id, l.Name, l.Color)).ToArray(), labels.Count > 50,
                members.Take(50).Select(m => new SearchMemberContext(m.UserId, m.DisplayName)).ToArray(), members.Count > 50));
        }
        return WorkOperation<BoardSearchPage>.Success(new(access.Value.Board.OrganizationId, boardId, documents,
            rows.Count > 50 ? documents[^1].Card.Id : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<BoardSearchPage>> SearchBoardAsync(Guid boardId, GlobalSearchBinding binding,
        Guid? after = null, CancellationToken cancellationToken = default)
    {
        var hint = await store.FindBoardAsync(boardId, cancellationToken);
        if (hint is null) return WorkOperation<BoardSearchPage>.Failure("board_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, binding.ActorId, "board_not_found",
            async () => await store.AcquireBoardReadScopeAsync(hint.OrganizationId, binding.ActorId, boardId, cancellationToken)
                && await inner.CheckCommandAccessAsync(boardId, binding.ActorId, "view", cancellationToken),
            async () =>
            {
                var result = await inner.SearchBoardAsync(boardId, binding, after, cancellationToken);
                return result.Succeeded && !await actors.VerifyAsync(binding.ActorId, cancellationToken)
                    ? WorkOperation<BoardSearchPage>.Failure("session_unavailable") : result;
            }, cancellationToken);
    }
}
