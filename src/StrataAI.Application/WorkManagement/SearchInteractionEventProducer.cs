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
    {
        if (actor == Guid.Empty)
            return Task.FromResult(IdentityOperation<SearchInteractionEvent>.Failure("session_unavailable"));
        return transactions.ExecuteAsync(actor, async () =>
        {
            var source = SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), actor, clock.UtcNow);
            try { await sources.AppendAsync(source, cancellationToken); }
            catch (InvalidOperationException error) when (error.Message == "Search interaction unavailable.")
            { return IdentityOperation<SearchInteractionEvent>.Failure("work_storage_unavailable"); }
            // A failed final original-session proof rolls back source and counter,
            // and does not return a canonical acknowledgment to the caller.
            if (!await actors.VerifyAsync(actor, cancellationToken))
                return IdentityOperation<SearchInteractionEvent>.Failure("session_unavailable");
            return IdentityOperation<SearchInteractionEvent>.Success(source);
        }, cancellationToken);
    }
}
