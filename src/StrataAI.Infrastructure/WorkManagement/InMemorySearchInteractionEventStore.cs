using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;

namespace StrataAI.Infrastructure.WorkManagement;

// Process-local Demo parity only. Personal sources never enter shared Work
// history. The identity unit owns rollback; Board checks borrow the Work gate.
internal sealed class InMemorySearchInteractionEventStore(DemoIdentityTransactionScope scope,
    IIdentityStore identities, IOrganizationStore organizations, IWorkManagementStore work,
    IWorkManagementUnitOfWork transactions) : ISearchInteractionEventStore, IDemoIdentityTransactionParticipant
{
    private readonly Dictionary<Guid, (long Sequence, SearchInteractionEvent Source)> _sources = [];
    private readonly Dictionary<Guid, long> _sequences = [];

    public async Task AppendAsync(SearchInteractionEvent source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!scope.Owns(source.ActorId))
            throw new InvalidOperationException("Search interaction requires the owning identity subject transaction.");
        cancellationToken.ThrowIfCancellationRequested();
        if (await identities.FindUserByIdAsync(source.ActorId, cancellationToken) is not { Status: AccountStatus.Active })
            throw new InvalidOperationException("Search interaction unavailable.");
        if (source.OrganizationId is not { } organization || source.BoardId is not { } board)
        {
            AppendOriginal(source); return;
        }
        async Task<bool> Admitted()
        {
            if (!await work.AcquireBoardReadScopeAsync(organization, source.ActorId, board, cancellationToken)) return false;
            if (await organizations.FindOrganizationAsync(organization, cancellationToken) is not { Status: OrganizationStatus.Active }
                || await work.FindBoardAsync(board, cancellationToken) is not { LifecycleState: BoardLifecycleState.Active } current
                || current.OrganizationId != organization) return false;
            var member = await organizations.FindMembershipAsync(organization, source.ActorId, cancellationToken);
            if (current.Visibility == BoardVisibility.Public) return true;
            if (member is not { Active: true }) return false;
            return current.Visibility != BoardVisibility.Private || member.Role is OrganizationRole.Owner or OrganizationRole.Admin
                || await work.FindBoardMemberAsync(board, source.ActorId, cancellationToken) is { Active: true };
        }
        var rollback = CaptureRollback();
        try
        {
            var result = await transactions.ExecuteReadAsync(organization, source.ActorId, "search_interaction_unavailable",
                Admitted, () => { AppendOriginal(source); return Task.FromResult(WorkOperation<bool>.Success(true)); }, cancellationToken);
            if (!result.Succeeded) throw new InvalidOperationException("Search interaction unavailable.");
        }
        catch
        {
            // The nested Work boundary does not own personal source state.
            // Restore it even if an identity caller catches this refusal.
            rollback(); throw;
        }
    }

    private void AppendOriginal(SearchInteractionEvent source)
    {
        if (_sources.TryGetValue(source.EventId, out var original))
        {
            var previous = original.Source;
            if (previous.ActorId != source.ActorId || previous.EventType != source.EventType
                || previous.OrganizationId != source.OrganizationId || previous.BoardId != source.BoardId
                || previous.CreatedAt != source.CreatedAt)
                throw new InvalidOperationException("Search interaction unavailable.");
            return;
        }
        // Refuse at capacity; never evict an original and permit a duplicate.
        if (_sources.Count >= 10000) throw new InvalidOperationException("Search interaction unavailable.");
        var sequence = checked(_sequences.GetValueOrDefault(source.ActorId) + 1);
        _sources.Add(source.EventId, (sequence, source)); _sequences[source.ActorId] = sequence;
    }

    public Action CaptureRollback()
    {
        var sources = DemoIdentityRollback.Dictionary(_sources);
        var sequences = DemoIdentityRollback.Dictionary(_sequences);
        return () => { sources(); sequences(); };
    }
}
