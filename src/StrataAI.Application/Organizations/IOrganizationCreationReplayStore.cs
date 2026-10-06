namespace StrataAI.Application.Organizations;

public sealed record OrganizationCreationReplay(string Fingerprint, OrganizationSummary Result, DateTimeOffset ExpiresAt);

public interface IOrganizationCreationReplayStore
{
    Task<OrganizationCreationReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken);
    Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationCreationReplay replay, CancellationToken cancellationToken);
}
