namespace StrataAI.Application.Organizations;

// Demo composition only. A page borrows the owning Organization transaction,
// its accepted request and rollback participants. Zero candidates proves only
// graph exhaustion; it does not publish Organization completion or readiness.
// Production continues to use leased pages in the separate Worker.
public interface IOrganizationDeletionGraphSimulation
{
    Task<int> ApplyPageAsync(Guid organizationId, Guid actorId, Guid requestId,
        long acceptedVersion, int pageSize, CancellationToken cancellationToken = default);
}
