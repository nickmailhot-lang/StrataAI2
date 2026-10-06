namespace StrataAI.Application.Organizations;

// Minimal request-bound status survives withdrawal of ordinary Organization
// reads. No names, graph counts, provider references or private content.
public sealed record OrganizationDeletionObservation(Guid RequestId, string State,
    long Version, Guid? EventId, DateTimeOffset? CompletedAt);

public interface IOrganizationDeletionObservationReader
{
    // Independently validates the current session and original requester/current
    // Owner; missing, mismatched or unavailable requests disclose no snapshot.
    Task<OrganizationOperation<OrganizationDeletionObservation>> ReadAsync(Guid organizationId,
        Guid actorId, Guid requestId, CancellationToken cancellationToken = default);
}
