using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

// Host-local Demo journal. Publication records the immutable accepted root,
// initial checkpoint and first job together; it does not claim processing.
internal sealed class InMemoryOrganizationDeletionJobPublisher(IOrganizationStore organizations,
    IIdentityStore identities, DemoWorkTransactionScope scope)
    : IOrganizationDeletionJobPublisher, IDemoOrganizationTransactionParticipant
{
    private sealed record Publication(OrganizationDeletionAttempt Root, Guid Actor,
        OrganizationDeletionAttempt Checkpoint, NewBackgroundJob FirstJob, OrganizationMetadataEvent? Completion = null);
    private readonly Dictionary<Guid, Publication> _publications = [];

    public async Task<bool> PublishAsync(Guid organizationId, Guid actorId, Guid requestId,
        long acceptedVersion, string correlationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!scope.OwnsOrganizationCommand(organizationId))
            throw new InvalidOperationException("Deletion publication requires its owning Organization transaction.");
        var attempt = new OrganizationDeletionAttempt(requestId, requestId, acceptedVersion);
        var job = OrganizationDeletionJobs.Create(organizationId, actorId, attempt, correlationId);
        var parent = await organizations.FindOrganizationAsync(organizationId, cancellationToken);
        var member = await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken);
        var user = await identities.FindUserByIdAsync(actorId, cancellationToken);
        if (parent is not { Status: OrganizationStatus.Deleting } || parent.Version != acceptedVersion
            || member is not { Active: true, Role: OrganizationRole.Owner } || user is not { Status: AccountStatus.Active })
            throw Unavailable();
        if (_publications.TryGetValue(organizationId, out var original))
        {
            if (original.Root != attempt || original.Actor != actorId) throw Unavailable();
            // Keep the original job ID, correlation, checkpoint and references.
            return false;
        }
        _publications.Add(organizationId, new(attempt, actorId, attempt, job));
        return true;
    }

    internal OrganizationDeletionAttempt? ReadAccepted(Guid organizationId, Guid actorId, Guid requestId)
    {
        if (!scope.OwnsOrganizationCommand(organizationId)) throw Unavailable();
        return _publications.TryGetValue(organizationId, out var publication)
            && publication.Actor == actorId && publication.Root.RequestId == requestId ? publication.Root : null;
    }

    public Action CaptureRollback()
    {
        var snapshot = _publications.ToArray();
        return () => { _publications.Clear(); foreach (var row in snapshot) _publications.Add(row.Key, row.Value); };
    }
    internal string ReadCorrelation(Guid organizationId, Guid actorId, Guid requestId)
    {
        if (ReadAccepted(organizationId, actorId, requestId) is null) throw Unavailable();
        return _publications[organizationId].FirstJob.CorrelationId;
    }
    // Caller owns both Demo gates; discovery exposes references internally only.
    internal (Guid Organization, Guid Actor, OrganizationDeletionAttempt Root)? NextPending(Guid? after)
    {
        var rows = _publications.Where(row => row.Value.Completion is null).OrderBy(row => row.Key);
        var next = rows.FirstOrDefault(row => after is null || row.Key.CompareTo(after.Value) > 0);
        if (next.Value is null) next = rows.FirstOrDefault();
        return next.Value is null ? null : (next.Key, next.Value.Actor, next.Value.Root);
    }
    internal OrganizationMetadataEvent? ReadCompletion(Guid organizationId)
    {
        if (!scope.OwnsOrganizationCommand(organizationId)) throw Unavailable();
        return _publications.GetValueOrDefault(organizationId)?.Completion;
    }
    internal OrganizationDeletionAttempt? ReadRoot(Guid organizationId)
    {
        if (!scope.OwnsOrganizationCommand(organizationId)) throw Unavailable();
        return _publications.GetValueOrDefault(organizationId)?.Root;
    }
    internal void Complete(Guid organizationId, Guid actor, Guid request, OrganizationMetadataEvent source)
    {
        if (!scope.OwnsAcceptedDeletion(organizationId, actor, request)
            || ReadAccepted(organizationId, actor, request) is not { } root
            || source.EventId == Guid.Empty || source.EventType != "ORGANIZATION_DELETED" || source.ActorId != actor
            || source.OrganizationId != organizationId || source.EntityId != organizationId || source.EntityType != "Organization"
            || source.Version != checked(root.AcceptedVersion + 1) || source.CreatedAt == default)
            throw Unavailable();
        var publication = _publications[organizationId];
        if (publication.Completion is not null) throw Unavailable();
        _publications[organizationId] = publication with { Completion = source };
    }
    private static OrganizationDeletionPublicationUnavailableException Unavailable() => new();
}
