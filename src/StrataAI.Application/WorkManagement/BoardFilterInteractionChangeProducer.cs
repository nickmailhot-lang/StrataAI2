using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.WorkManagement;

// Only actual Apply/Clear intents call this producer, after filter validation
// and Board admission. Pagination, refresh and anonymous reads do not call it.
public sealed class BoardFilterInteractionChangeProducer(IBoardFilterInteractionReplayStore replays,
    ISearchInteractionEventStore sources, IIdentityUnitOfWork transactions,
    ICommandActorAuthorization actors, IClock clock)
{
    public Task<IdentityOperation<SearchInteractionEvent>> ProduceAsync(Guid actor, Guid organization, Guid board,
        Guid requestId, string fingerprint, CancellationToken cancellationToken = default)
    {
        if (actor == Guid.Empty) return Task.FromResult(IdentityOperation<SearchInteractionEvent>.Failure("session_unavailable"));
        if (organization == Guid.Empty || board == Guid.Empty || requestId == Guid.Empty || fingerprint is null
            || fingerprint.Length != 64 || fingerprint.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return Task.FromResult(IdentityOperation<SearchInteractionEvent>.Failure("invalid_search"));
        return transactions.ExecuteObservationAsync(actor, organization, async () =>
        {
            var candidate = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), actor, organization, board, clock.UtcNow);
            SearchInteractionEvent original;
            try { original = await replays.AppendOrReplayAsync(requestId, fingerprint, candidate, cancellationToken); }
            catch (InvalidOperationException error) when (error.Message == "Search interaction unavailable.")
            { return IdentityOperation<SearchInteractionEvent>.Failure("work_storage_unavailable"); }
            if (original.ActorId != actor || original.OrganizationId != organization || original.BoardId != board
                || original.EventType != "BOARD_FILTER_CHANGED")
                return IdentityOperation<SearchInteractionEvent>.Failure("work_storage_unavailable");
            // Both fresh writes and stored originals require the original
            // request session, followed by current Board proof after that IO.
            if (!await actors.VerifyAsync(actor, cancellationToken))
                return IdentityOperation<SearchInteractionEvent>.Failure("session_unavailable");
            try { await sources.AppendAsync(original, cancellationToken); }
            catch (InvalidOperationException error) when (error.Message == "Search interaction unavailable.")
            { return IdentityOperation<SearchInteractionEvent>.Failure("work_storage_unavailable"); }
            return IdentityOperation<SearchInteractionEvent>.Success(original);
        }, cancellationToken);
    }
}
