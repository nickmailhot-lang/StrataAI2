using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationDeletionObservationReader(InMemoryOrganizationDeletionJobPublisher journal,
    IOrganizationStore organizations, ICommandActorAuthorization actors, InMemoryAccountOrganizationGate gate,
    DemoWorkTransactionScope scope) : IOrganizationDeletionObservationReader
{
    public async Task<OrganizationOperation<OrganizationDeletionObservation>> ReadAsync(Guid organizationId,
        Guid actorId, Guid requestId, CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(organizationId==Guid.Empty || actorId==Guid.Empty || requestId==Guid.Empty)return Missing();
        await gate.Commands.WaitAsync(cancellationToken);
        try
        {
            await gate.WorkCommands.WaitAsync(cancellationToken);
            try
            {
                using var owned=scope.Enter(organizationId,organizationCommand:true);
                var member=await organizations.FindMembershipAsync(organizationId,actorId,cancellationToken);
                if(member is not {Active:true,Role:OrganizationRole.Owner})return Missing();
                if(!await actors.VerifyAsync(actorId,cancellationToken))return SessionUnavailable();
                var parent=await organizations.FindOrganizationAsync(organizationId,cancellationToken);
                var request=journal.ReadAccepted(organizationId,actorId,requestId);
                if(request is null || parent is null)return Missing();
                OrganizationDeletionObservation? observed = null;
                var terminal = journal.ReadCompletion(organizationId);
                if(parent.Status == OrganizationStatus.Deleting && parent.Version == request.AcceptedVersion && terminal is null)
                    observed = new(requestId,"PENDING",parent.Version,null,null);
                if(parent.Status == OrganizationStatus.Deleted && parent.Version == checked(request.AcceptedVersion + 1)
                    && terminal is not null && terminal.ActorId == actorId && terminal.Version == parent.Version
                    && terminal.CreatedAt == parent.UpdatedAt
                    && ((InMemoryOrganizationStore)organizations).MatchesDeletionAttribution(organizationId, actorId, terminal.CreatedAt))
                    observed = new(requestId,"COMPLETED",parent.Version,terminal.EventId,terminal.CreatedAt);
                if(observed is null)return Missing();
                if(!await actors.VerifyAsync(actorId,cancellationToken))return SessionUnavailable();
                return OrganizationOperation<OrganizationDeletionObservation>.Success(observed);
            }
            finally{gate.WorkCommands.Release();}
        }
        finally{gate.Commands.Release();}
    }
    private static OrganizationOperation<OrganizationDeletionObservation> Missing()=>OrganizationOperation<OrganizationDeletionObservation>.Failure("organization_not_found");
    private static OrganizationOperation<OrganizationDeletionObservation> SessionUnavailable()=>OrganizationOperation<OrganizationDeletionObservation>.Failure("session_unavailable");
}
