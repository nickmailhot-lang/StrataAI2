namespace StrataAI.Application.BackgroundJobs;

// Counts persisted states, including scheduled pending work and expired leases.
// No job references, payloads or tenant labels cross this diagnostic boundary.
public sealed record OrganizationJobBacklog(long Pending, long Running);

public interface IOrganizationJobBacklogReader
{
    Task<OrganizationJobBacklog> ReadAsync(Guid organizationId, CancellationToken cancellationToken);
}
