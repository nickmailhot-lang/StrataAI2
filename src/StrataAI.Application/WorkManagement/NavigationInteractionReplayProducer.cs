using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.WorkManagement;

// Actor-owned receipts bind request identity to the canonical intent digest.
// Implementations must reauthorize the current target before returning an
// original, retain its event ID/clock, and roll back receipts with source writes.
public interface INavigationInteractionReplayStore
{
    Task<NavigationInteractionEvent?> AppendOrReplayAuthorizedAsync(Guid requestId, string fingerprint,
        NavigationInteractionEvent candidate, CancellationToken cancellationToken = default);
}

public sealed class NavigationInteractionReplayProducer(INavigationInteractionReplayStore receipts,
    IIdentityUnitOfWork transactions, ICommandActorAuthorization actors, IClock clock)
{
    public Task<IdentityOperation<NavigationInteractionEvent>> ContextAsync(Guid actor, Guid? organization,
        Guid requestId, string fingerprint, CancellationToken ct = default) => ProduceAsync(actor, requestId, fingerprint,
            () => NavigationInteractionEvent.ApplicationContextChanged(Guid.NewGuid(), actor, organization, clock.UtcNow), ct);
    public Task<IdentityOperation<NavigationInteractionEvent>> BoardAsync(Guid actor, Guid organization, Guid board,
        long version, Guid requestId, string fingerprint, CancellationToken ct = default) => ProduceAsync(actor, requestId, fingerprint,
            () => NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, organization, board, version, clock.UtcNow), ct);
    public Task<IdentityOperation<NavigationInteractionEvent>> CardAsync(Guid actor, Guid organization, Guid board,
        Guid card, long version, Guid requestId, string fingerprint, CancellationToken ct = default) => ProduceAsync(actor, requestId, fingerprint,
            () => NavigationInteractionEvent.CardOpened(Guid.NewGuid(), actor, organization, board, card, version, clock.UtcNow), ct);

    private Task<IdentityOperation<NavigationInteractionEvent>> ProduceAsync(Guid actor, Guid requestId,
        string fingerprint, Func<NavigationInteractionEvent> create, CancellationToken ct)
    {
        if (actor == Guid.Empty) return Task.FromResult(IdentityOperation<NavigationInteractionEvent>.Failure("session_unavailable"));
        if (requestId == Guid.Empty || fingerprint is null || fingerprint.Length != 64
            || fingerprint.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return Task.FromResult(IdentityOperation<NavigationInteractionEvent>.Failure("invalid_navigation"));
        NavigationInteractionEvent candidate;
        try { candidate = create(); }
        catch (ArgumentException) { return Task.FromResult(IdentityOperation<NavigationInteractionEvent>.Failure("invalid_navigation")); }
        return transactions.ExecuteObservationAsync(actor, candidate.OrganizationId, async () => {
            if (!await actors.VerifyAsync(actor, ct)) return IdentityOperation<NavigationInteractionEvent>.Failure("session_unavailable");
            var original = await receipts.AppendOrReplayAuthorizedAsync(requestId, fingerprint, candidate, ct);
            if (original is null || original.ActorId != actor || original.EventType != candidate.EventType
                || original.OrganizationId != candidate.OrganizationId || original.BoardId != candidate.BoardId
                || original.EntityType != candidate.EntityType || original.Version != candidate.Version
                || (candidate.EntityType != "ApplicationContext" && original.EntityId != candidate.EntityId))
                return IdentityOperation<NavigationInteractionEvent>.Failure("navigation_unavailable");
            if (!await actors.VerifyAsync(actor, ct)) return IdentityOperation<NavigationInteractionEvent>.Failure("session_unavailable");
            return IdentityOperation<NavigationInteractionEvent>.Success(original);
        }, ct);
    }
}
