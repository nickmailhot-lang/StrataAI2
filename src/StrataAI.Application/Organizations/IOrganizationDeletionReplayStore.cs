namespace StrataAI.Application.Organizations;

public sealed record OrganizationDeletionReplay(string Fingerprint, DateTimeOffset ExpiresAt);
public interface IOrganizationDeletionReplayStore
{
    Task<OrganizationDeletionReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationDeletionReplay replay, CancellationToken cancellationToken);
}
