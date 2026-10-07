using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationDeletionGraphSimulation(InMemoryWorkManagementStore work,
    IOrganizationStore organizations, IIdentityStore identities, InMemoryOrganizationDeletionJobPublisher journal,
    DemoWorkTransactionScope scope, IWorkEventStore events, IClock clock)
    : IOrganizationDeletionGraphSimulation, IDemoOrganizationTransactionParticipant
{
    // Immutable history survives descendant tombstones for this API process.
    private readonly Dictionary<Guid, DemoInvitationAudit> _audits = [];

    public async Task<int> ApplyPageAsync(Guid organizationId, Guid actorId, Guid requestId,
        long acceptedVersion, int pageSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!scope.OwnsOrganizationCommand(organizationId) || organizationId == Guid.Empty
            || actorId == Guid.Empty || requestId == Guid.Empty || pageSize is < 1 or > OrganizationDeletionJobs.PageSize)
            throw new OrganizationDeletionPublicationUnavailableException();
        var root = journal.ReadAccepted(organizationId, actorId, requestId);
        var parent = await organizations.FindOrganizationAsync(organizationId, cancellationToken);
        if (root?.AcceptedVersion != acceptedVersion || parent is not { Status: OrganizationStatus.Deleting }
            || parent.Version != acceptedVersion
            || await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken) is not { Active: true, Role: OrganizationRole.Owner }
            || await identities.FindUserByIdAsync(actorId, cancellationToken) is not { Status: AccountStatus.Active })
            throw new OrganizationDeletionPublicationUnavailableException();
        var correlation = journal.ReadCorrelation(organizationId, actorId, requestId);
        var page = work.ApplyOrganizationDeletionGraphPage(organizationId, actorId, pageSize,
            parent.UpdatedAt > clock.UtcNow ? parent.UpdatedAt : clock.UtcNow, correlation, cancellationToken);
        foreach (var audit in page.Audits) _audits.Add(audit.Id, audit);
        foreach (var change in page.Events) await events.AppendAsync(change, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return page.Count;
    }

    public Action CaptureRollback() => DemoRollback.Dictionary(_audits);
}
