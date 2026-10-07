using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationLifecycleEventReader(InMemoryOrganizationStore organizations,
    InMemoryOrganizationDeletionJobPublisher journal, IIdentityStore identities, ICommandActorAuthorization actors,
    InMemoryAccountOrganizationGate gate, DemoWorkTransactionScope scope) : IOrganizationLifecycleEventReader
{
    public async Task<OrganizationOperation<OrganizationLifecyclePage>> ReadAsync(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (organizationId == Guid.Empty || actorId == Guid.Empty) return Missing();
        await gate.Commands.WaitAsync(cancellationToken);
        try
        {
            await gate.WorkCommands.WaitAsync(cancellationToken);
            try
            {
                using var owned = scope.Enter(organizationId, organizationCommand: true);
                var parent = await organizations.FindOrganizationAsync(organizationId, cancellationToken);
                if (parent is null || parent.Status is not (OrganizationStatus.Active or OrganizationStatus.Deleting or OrganizationStatus.Deleted)
                    || await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken) is not { Active: true }) return Missing();
                if (await identities.FindUserByIdAsync(actorId, cancellationToken) is not { Status: AccountStatus.Active }
                    || !await actors.VerifyAsync(actorId, cancellationToken)) return SessionUnavailable();
                OrganizationLifecyclePage? page = parent.Status == OrganizationStatus.Active ? new("ACTIVE", []) : null;
                var root = journal.ReadRoot(organizationId); var terminal = journal.ReadCompletion(organizationId);
                if (root is not null && parent.Status == OrganizationStatus.Deleting && parent.Version == root.AcceptedVersion && terminal is null)
                    page = new("PENDING", []);
                if (root is not null && terminal is not null && parent.Status == OrganizationStatus.Deleted
                    && parent.Version == checked(root.AcceptedVersion + 1) && terminal.Version == parent.Version
                    && terminal.CreatedAt == parent.UpdatedAt && organizations.MatchesDeletionAttribution(organizationId, terminal.ActorId, terminal.CreatedAt)
                    && journal.ReadAccepted(organizationId, terminal.ActorId, root.RequestId) == root)
                    page = new("COMPLETED", [terminal]);
                if (page is null) return Missing();
                if (!await actors.VerifyAsync(actorId, cancellationToken)) return SessionUnavailable();
                return OrganizationOperation<OrganizationLifecyclePage>.Success(page);
            }
            finally { gate.WorkCommands.Release(); }
        }
        finally { gate.Commands.Release(); }
    }
    private static OrganizationOperation<OrganizationLifecyclePage> Missing() => OrganizationOperation<OrganizationLifecyclePage>.Failure("organization_not_found");
    private static OrganizationOperation<OrganizationLifecyclePage> SessionUnavailable() => OrganizationOperation<OrganizationLifecyclePage>.Failure("session_unavailable");
}
