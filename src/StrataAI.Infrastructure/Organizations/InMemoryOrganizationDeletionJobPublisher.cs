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
        OrganizationDeletionAttempt Checkpoint, NewBackgroundJob FirstJob);
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
    private static OrganizationDeletionPublicationUnavailableException Unavailable() => new();
}
