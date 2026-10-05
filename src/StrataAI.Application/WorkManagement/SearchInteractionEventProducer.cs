using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.WorkManagement;

public sealed class SearchInteractionEventProducer(ISearchInteractionEventStore sources,
    IIdentityUnitOfWork transactions, ICommandActorAuthorization actors, IClock clock)
{
    // Called only after successful search validation/disclosure admission. The
    // caller supplies its authenticated actor, never client-authored event data.
    // Do not wrap cross-Organization search traversal in an identity transaction:
    // its independent tenant read boundaries must finish before this append.
    public Task<IdentityOperation<SearchInteractionEvent>> SearchExecutedAsync(Guid actor,
        CancellationToken cancellationToken = default)
        => ProduceAsync(actor, () => SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), actor, clock.UtcNow), cancellationToken);

    // A change-intent caller must first validate the filter and obtain this
    // actual scope from an admitted result. Background/pagination reads must
    // not call this method merely because they fetched filtered Cards.
    public Task<IdentityOperation<SearchInteractionEvent>> BoardFilterChangedAsync(Guid actor, Guid organization, Guid board,
        CancellationToken cancellationToken = default)
    {
        if (organization == Guid.Empty || board == Guid.Empty)
            return Task.FromResult(IdentityOperation<SearchInteractionEvent>.Failure("invalid_search"));
        return ProduceAsync(actor, () => SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), actor, organization, board, clock.UtcNow), cancellationToken);
    }

    private Task<IdentityOperation<SearchInteractionEvent>> ProduceAsync(Guid actor, Func<SearchInteractionEvent> create,
        CancellationToken cancellationToken)
    {
        if (actor == Guid.Empty)
            return Task.FromResult(IdentityOperation<SearchInteractionEvent>.Failure("session_unavailable"));
        return transactions.ExecuteAsync(actor, async () =>
        {
            var source = create();
            try { await sources.AppendAsync(source, cancellationToken); }
            catch (InvalidOperationException error) when (error.Message == "Search interaction unavailable.")
            { return IdentityOperation<SearchInteractionEvent>.Failure("work_storage_unavailable"); }
            // A failed final original-session proof rolls back source and counter,
            // and does not return a canonical acknowledgment to the caller.
            if (!await actors.VerifyAsync(actor, cancellationToken))
                return IdentityOperation<SearchInteractionEvent>.Failure("session_unavailable");
            if (source.BoardId is not null)
            {
                // Session proof can await external IO. Freshly re-admit the
                // identical Board source afterward; this must not allocate a
                // second original or advance its clock/sequence.
                try { await sources.AppendAsync(source, cancellationToken); }
                catch (InvalidOperationException error) when (error.Message == "Search interaction unavailable.")
                { return IdentityOperation<SearchInteractionEvent>.Failure("work_storage_unavailable"); }
            }
            return IdentityOperation<SearchInteractionEvent>.Success(source);
        }, cancellationToken);
    }
}
