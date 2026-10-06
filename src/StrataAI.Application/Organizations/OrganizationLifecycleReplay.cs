namespace StrataAI.Application.Organizations;

// A bounded terminal channel for current Internal members. It never exposes
// deletion request keys, private graph content or the Owner's command receipt.
public sealed record OrganizationLifecyclePage(string State, IReadOnlyList<OrganizationMetadataEvent> Events);
public interface IOrganizationLifecycleEventReader
{
    Task<OrganizationOperation<OrganizationLifecyclePage>> ReadAsync(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default);
}
