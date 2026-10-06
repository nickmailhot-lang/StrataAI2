using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.WorkManagement;

// The store borrows the owning identity transaction, proves current target
// visibility and appends once. Denied targets must not change stream counters.
// It must recheck scope for originals as well as fresh observations.
public interface INavigationInteractionEventStore
{
    Task<bool> AppendAuthorizedAsync(NavigationInteractionEvent source,
        CancellationToken cancellationToken = default);
}

public sealed class NavigationInteractionEventProducer(INavigationInteractionEventStore sources,
    IIdentityUnitOfWork transactions, ICommandActorAuthorization actors, IClock clock)
{
    public Task<IdentityOperation<NavigationInteractionEvent>> ApplicationContextChangedAsync(Guid actor,
        Guid? organization, CancellationToken ct = default) => ProduceAsync(actor,
            () => NavigationInteractionEvent.ApplicationContextChanged(Guid.NewGuid(), actor, organization, clock.UtcNow), ct);
    public Task<IdentityOperation<NavigationInteractionEvent>> BoardOpenedAsync(Guid actor, Guid organization,
        Guid board, long version, CancellationToken ct = default) => ProduceAsync(actor,
            () => NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, organization, board, version, clock.UtcNow), ct);
    public Task<IdentityOperation<NavigationInteractionEvent>> CardOpenedAsync(Guid actor, Guid organization,
        Guid board, Guid card, long version, CancellationToken ct = default) => ProduceAsync(actor,
            () => NavigationInteractionEvent.CardOpened(Guid.NewGuid(), actor, organization, board, card, version, clock.UtcNow), ct);

    private Task<IdentityOperation<NavigationInteractionEvent>> ProduceAsync(Guid actor,
        Func<NavigationInteractionEvent> create, CancellationToken ct)
    {
        if (actor == Guid.Empty)
            return Task.FromResult(IdentityOperation<NavigationInteractionEvent>.Failure("session_unavailable"));
        NavigationInteractionEvent source;
        try { source = create(); }
        catch (ArgumentException) { return Task.FromResult(IdentityOperation<NavigationInteractionEvent>.Failure("invalid_navigation")); }
        return transactions.ExecuteObservationAsync(actor, source.OrganizationId, async () => {
            if (!await actors.VerifyAsync(actor, ct))
                return IdentityOperation<NavigationInteractionEvent>.Failure("session_unavailable");
            if (!await sources.AppendAuthorizedAsync(source, ct))
                return IdentityOperation<NavigationInteractionEvent>.Failure("navigation_unavailable");
            // Loss of the original session after storage rolls back the source.
            if (!await actors.VerifyAsync(actor, ct))
                return IdentityOperation<NavigationInteractionEvent>.Failure("session_unavailable");
            return IdentityOperation<NavigationInteractionEvent>.Success(source);
        }, ct);
    }
}
