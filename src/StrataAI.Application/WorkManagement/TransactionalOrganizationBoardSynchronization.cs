using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed class TransactionalOrganizationBoardSynchronization(OrganizationBoardSynchronizationService replay,
    IWorkManagementUnitOfWork transactions, IWorkManagementStore work, IOrganizationStore organizations)
{
    public Task<WorkOperation<OrganizationBoardSyncPage>> ReadAsync(Guid organizationId, Guid actorId,
        string? cursor, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty)
            return Task.FromResult(WorkOperation<OrganizationBoardSyncPage>.Failure("organization_not_found"));
        return transactions.ExecuteReadAsync(organizationId, actorId, "organization_not_found", async () =>
        {
            if (!await work.AcquireOrganizationReadScopeAsync(organizationId, actorId, cancellationToken)) return false;
            return await organizations.FindOrganizationAsync(organizationId, cancellationToken) is { Status: OrganizationStatus.Active } &&
                await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken) is { Active: true };
        }, () => replay.ReadAsync(organizationId, actorId, cursor, limit, cancellationToken), cancellationToken);
    }
}
