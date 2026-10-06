namespace StrataAI.Application.Organizations;

public sealed record OrganizationDepartureReplay(DateTimeOffset ExpiresAt);
public interface IOrganizationDepartureReplayStore
{
    Task<OrganizationDepartureReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationDepartureReplay replay, CancellationToken cancellationToken);
}
