using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<CardRecord>> CopyCardAsync(Guid cardId, Guid sourceBoardId, Guid destinationListId,
        Guid actorId, string title, long expectedVersion, string correlationId, CancellationToken cancellationToken = default)
    {
        var source = await store.FindCardAsync(cardId, cancellationToken);
        var target = await store.FindListAsync(destinationListId, cancellationToken);
        var parent = source is null ? null : await store.FindListAsync(source.ListId, cancellationToken);
        if (source is not { LifecycleState: WorkItemLifecycleState.Active } || source.BoardId != sourceBoardId
            || target is not { LifecycleState: WorkItemLifecycleState.Active } || parent is not { LifecycleState: WorkItemLifecycleState.Active }
            || source.OrganizationId != target.OrganizationId || parent.BoardId != source.BoardId)
            return WorkOperation<CardRecord>.Failure("card_not_found");
        var from = await ResolveAccessAsync(source.BoardId, actorId, cancellationToken);
        var to = await ResolveAccessAsync(target.BoardId, actorId, cancellationToken);
        if (from is not { Access.CanEdit: true, Board.LifecycleState: BoardLifecycleState.Active }
            || to is not { Access.CanEdit: true, Board.LifecycleState: BoardLifecycleState.Active })
            return WorkOperation<CardRecord>.Failure("card_not_found");
        if (!TryNormalizeCardTitle(title, out var normalizedTitle)) return WorkOperation<CardRecord>.Failure("invalid_card_title");
        if (expectedVersion <= 0 || source.Version != expectedVersion) return WorkOperation<CardRecord>.Failure("version_conflict");
        CardRecord? copied;
        try { copied = await store.CopyCardAsync(cardId, destinationListId, Guid.NewGuid(), normalizedTitle,
            expectedVersion, clock.UtcNow, cancellationToken); }
        catch (RankSpaceExhaustedException) { return WorkOperation<CardRecord>.Failure("rank_space_exhausted"); }
        if (copied is null) return WorkOperation<CardRecord>.Failure("version_conflict");
        await RecordChangeAsync(copied.OrganizationId, copied.BoardId, actorId, "CARD_COPIED", "Card", copied.Id,
            copied.Version, correlationId, cancellationToken);
        return WorkOperation<CardRecord>.Success(copied);
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<CardRecord>> CopyCardAsync(Guid cardId, Guid sourceBoardId, Guid destinationListId,
        Guid actorId, string title, long expectedVersion, string correlationId, CancellationToken cancellationToken = default)
    {
        var source = await store.FindCardAsync(cardId, cancellationToken);
        var target = await store.FindListAsync(destinationListId, cancellationToken);
        var original = await store.FindBoardAsync(sourceBoardId, cancellationToken);
        if (source is null || target is null || original is null || source.OrganizationId != target.OrganizationId
            || original.OrganizationId != source.OrganizationId) return WorkOperation<CardRecord>.Failure("card_not_found");
        return await transactions.ExecuteAsync(source.OrganizationId,
            WorkCommand.Create(actorId, context.IdempotencyKey, "CopyCardAsync", cardId,
                new { sourceBoardId, destinationListId, title, expectedVersion }, "card_not_found"), async receipt =>
            {
                // Plan every original/current gate before taking the first Board.
                // A copied Card may itself have moved since the original reply.
                var copyHint = receipt is null ? null : await store.FindCardAsync(receipt.Id, cancellationToken);
                if (receipt is not null && (receipt.Id == cardId || receipt.OrganizationId != source.OrganizationId
                    || receipt.BoardId != target.BoardId || receipt.ListId != destinationListId || receipt.Version != 1
                    || copyHint is null || copyHint.OrganizationId != source.OrganizationId)) return false;
                var gates = new[] { sourceBoardId, source.BoardId, target.BoardId }
                    .Concat(copyHint is null ? [] : new[] { copyHint.BoardId }).Distinct().Order().ToArray();
                foreach (var gate in gates)
                    if (!await AuthorizeBoard(gate, actorId, "edit", cancellationToken)
                        || (await store.FindBoardAsync(gate, cancellationToken)) is not { LifecycleState: BoardLifecycleState.Active } board
                        || board.OrganizationId != source.OrganizationId) return false;
                var current = await store.FindCardAsync(cardId, cancellationToken);
                var destination = await store.FindListAsync(destinationListId, cancellationToken);
                var parent = current is null ? null : await store.FindListAsync(current.ListId, cancellationToken);
                if (current is not { LifecycleState: WorkItemLifecycleState.Active } || current.OrganizationId != source.OrganizationId
                    || current.BoardId != source.BoardId || parent is not { LifecycleState: WorkItemLifecycleState.Active }
                    || parent.BoardId != current.BoardId || parent.OrganizationId != source.OrganizationId
                    || destination is not { LifecycleState: WorkItemLifecycleState.Active } || destination.BoardId != target.BoardId
                    || destination.OrganizationId != source.OrganizationId || !await actors.VerifyAsync(actorId, cancellationToken)) return false;
                if (copyHint is null) return true;
                var currentCopy = await store.FindCardAsync(copyHint.Id, cancellationToken);
                var copyParent = currentCopy is null ? null : await store.FindListAsync(currentCopy.ListId, cancellationToken);
                return currentCopy is not null && currentCopy.OrganizationId == source.OrganizationId
                    && currentCopy.BoardId == copyHint.BoardId && currentCopy.Version >= receipt!.Version
                    && copyParent is { LifecycleState: WorkItemLifecycleState.Active } && copyParent.BoardId == currentCopy.BoardId
                    && copyParent.OrganizationId == source.OrganizationId;
            }, async () =>
            {
                var result = await inner.CopyCardAsync(cardId, sourceBoardId, destinationListId, actorId, title,
                    expectedVersion, correlationId, cancellationToken);
                return await actors.VerifyAsync(actorId, cancellationToken) ? result : WorkOperation<CardRecord>.Failure("session_unavailable");
            }, cancellationToken);
    }
}
