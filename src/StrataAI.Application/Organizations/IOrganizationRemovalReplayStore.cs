namespace StrataAI.Application.Organizations;

public sealed record OrganizationRemovalReplay(string Fingerprint, DateTimeOffset ExpiresAt);
public interface IOrganizationRemovalReplayStore
{
    Task<OrganizationRemovalReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationRemovalReplay replay, CancellationToken cancellationToken);
}
