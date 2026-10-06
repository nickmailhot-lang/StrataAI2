using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo only. Identity rollback owns private originals; tenant read boundaries
// recheck current target admission without changing Board or Card revisions.
internal sealed class InMemoryNavigationInteractionEventStore(DemoIdentityTransactionScope scope,
    IIdentityStore identities, IOrganizationStore organizations,
    IWorkManagementStore work, IWorkManagementUnitOfWork transactions, IClock clock)
    : INavigationInteractionEventStore, INavigationInteractionReplayStore, IDemoIdentityTransactionParticipant
{
    private readonly Dictionary<Guid, NavigationInteractionEvent> _sources = [];
    private readonly Dictionary<(Guid Actor, Guid Request), (string Fingerprint, NavigationInteractionEvent Source, DateTimeOffset Expires)> _receipts = [];
    public async Task<NavigationInteractionEvent?> AppendOrReplayAuthorizedAsync(Guid requestId, string fingerprint,
        NavigationInteractionEvent candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!scope.Owns(candidate.ActorId)) throw new InvalidOperationException("Navigation requires owning identity transaction.");
        if (requestId == Guid.Empty || fingerprint is null || fingerprint.Length != 64
            || fingerprint.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) return null;
        var rollback = CaptureRollback();
        try {
            var key = (candidate.ActorId, requestId);
            if (_receipts.TryGetValue(key, out var receipt)) {
                var original = receipt.Source;
                if (receipt.Fingerprint != fingerprint || receipt.Expires <= clock.UtcNow
                    || original.EventType != candidate.EventType || original.OrganizationId != candidate.OrganizationId
                    || original.BoardId != candidate.BoardId || original.EntityType != candidate.EntityType
                    || original.Version != candidate.Version
                    || (original.EntityType != "ApplicationContext" && original.EntityId != candidate.EntityId)) return null;
                return await AppendAuthorizedAsync(original, ct) ? original : null;
            }
            if (!await AppendAuthorizedAsync(candidate, ct)) { rollback(); return null; }
            var now = clock.UtcNow;
            foreach (var expired in _receipts.Where(r => r.Key.Actor == candidate.ActorId && r.Value.Expires <= now)
                .OrderBy(r => r.Value.Expires).ThenBy(r => r.Key.Request.ToString("D"), StringComparer.Ordinal)
                .Take(100).Select(r => r.Key).ToArray()) _receipts.Remove(expired);
            if (_receipts.Count(r => r.Key.Actor == candidate.ActorId) >= 1000
                || _receipts.Values.Any(r => r.Source.EventId == candidate.EventId)) { rollback(); return null; }
            _receipts.Add(key, (fingerprint, candidate, now.AddHours(24))); return candidate;
        } catch { rollback(); throw; }
    }
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
                // The owning identity transaction already holds the shared
                // account/Organization gate; reacquiring it would deadlock.
                if (await organizations.FindOrganizationAsync(org, ct) is not { Status: OrganizationStatus.Active }
                    || await organizations.FindMembershipAsync(org, source.ActorId, ct) is not { Active: true }) return false;
                return Append(source);
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
    public Action CaptureRollback()
    {
        var sources = DemoIdentityRollback.Dictionary(_sources);
        var receipts = DemoIdentityRollback.Dictionary(_receipts);
        return () => { sources(); receipts(); };
    }
}
