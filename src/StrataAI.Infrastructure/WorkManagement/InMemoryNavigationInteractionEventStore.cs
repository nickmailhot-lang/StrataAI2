using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo only. Identity rollback owns private originals; tenant read boundaries
// recheck current target admission without changing Board or Card revisions.
internal sealed class InMemoryNavigationInteractionEventStore(DemoIdentityTransactionScope scope,
    IIdentityStore identities, IOrganizationStore organizations, IOrganizationUnitOfWork organizationTransactions,
    IWorkManagementStore work, IWorkManagementUnitOfWork transactions)
    : INavigationInteractionEventStore, IDemoIdentityTransactionParticipant
{
    private readonly Dictionary<Guid, NavigationInteractionEvent> _sources = [];
    public async Task<bool> AppendAuthorizedAsync(NavigationInteractionEvent source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!scope.Owns(source.ActorId)) throw new InvalidOperationException("Navigation requires owning identity transaction.");
        ct.ThrowIfCancellationRequested();
        if (await identities.FindUserByIdAsync(source.ActorId, ct) is not { Status: AccountStatus.Active }) return false;
        var rollback = CaptureRollback();
        try {
            if (source.OrganizationId is not { } org) return Append(source);
            if (source.BoardId is not { } board) {
                var result = await organizationTransactions.ExecuteAsync(org, source.ActorId, null, false, async () => {
                    if (await organizations.FindOrganizationAsync(org, ct) is not { Status: OrganizationStatus.Active }
                        || await organizations.FindMembershipAsync(org, source.ActorId, ct) is not { Active: true })
                        return OrganizationOperation<bool>.Failure("organization_unavailable");
                    return OrganizationOperation<bool>.Success(Append(source));
                }, ct);
                if (!result.Succeeded) { rollback(); return false; }
                return result.Value;
            }
            async Task<bool> Admitted() {
                if (!await work.AcquireBoardReadScopeAsync(org, source.ActorId, board, ct)
                    || await organizations.FindOrganizationAsync(org, ct) is not { Status: OrganizationStatus.Active }
                    || await work.FindBoardAsync(board, ct) is not { LifecycleState: BoardLifecycleState.Active } current
                    || current.OrganizationId != org) return false;
                var member = await organizations.FindMembershipAsync(org, source.ActorId, ct);
                if (current.Visibility != BoardVisibility.Public && (member is not { Active: true }
                    || (current.Visibility == BoardVisibility.Private && member.Role is not (OrganizationRole.Owner or OrganizationRole.Admin)
                        && await work.FindBoardMemberAsync(board, source.ActorId, ct) is not { Active: true }))) return false;
                if (source.EntityType == "Board") return current.Id == source.EntityId && current.Version == source.Version;
                var card = await work.FindCardAsync(source.EntityId, ct);
                if (card is null || card.BoardId != board || card.OrganizationId != org || card.Version != source.Version
                    || card.LifecycleState != WorkItemLifecycleState.Active) return false;
                return await work.FindListAsync(card.ListId, ct) is { LifecycleState: WorkItemLifecycleState.Active } list
                    && list.BoardId == board && list.OrganizationId == org;
            }
            var admitted = await transactions.ExecuteReadAsync(org, source.ActorId, "navigation_unavailable", Admitted,
                () => Task.FromResult(WorkOperation<bool>.Success(Append(source))), ct);
            if (!admitted.Succeeded) { rollback(); return false; }
            return admitted.Value;
        } catch { rollback(); throw; }
    }
    private bool Append(NavigationInteractionEvent source)
    {
        if (_sources.TryGetValue(source.EventId, out var original)) return original == source;
        if (_sources.Count >= 10000) return false;
        _sources.Add(source.EventId, source); return true;
    }
    public Action CaptureRollback() => DemoIdentityRollback.Dictionary(_sources);
}
