namespace StrataAI.Application.Organizations;

public sealed record OrganizationMetadataReplay(string Fingerprint, OrganizationRecord Result, DateTimeOffset ExpiresAt);

public interface IOrganizationMetadataReplayStore
{
    Task<OrganizationMetadataReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationMetadataReplay replay, CancellationToken cancellationToken);
}
