using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Organizations;

internal sealed record DemoOrganizationAuthorityProof(Guid OrganizationId, string EntityType,
    Guid EntityId, string EventType, long Version, DateTimeOffset CreatedAt, Guid CommandId, OrganizationRole? PreviousRole);

internal sealed partial class InMemoryOrganizationStore
{
    private readonly Dictionary<(Guid Organization, string EntityType, Guid Entity), DemoOrganizationAuthorityProof> _authorityProofs = [];

    private void CaptureAuthorityProof(Guid organization, string entityType, Guid entity, string eventType,
        long version, DateTimeOffset at, OrganizationRole? previousRole = null)
    {
        // Legacy fixture writes outside owning commands do not acquire history.
        if (workScope.OwnsOrganizationCommand(organization))
            _authorityProofs[(organization, entityType, entity)] = new(organization, entityType, entity, eventType, version, at, workScope.CommandId, previousRole);
    }

    internal DemoOrganizationAuthorityProof RequireAuthorityProof(Guid organization, string entityType, Guid entity, string eventType)
    {
        var proof = RequireTransitionProof(organization, entityType, entity, eventType);
        if (proof.Version < 2) throw new InvalidOperationException("Organization authority transition is unproven.");
        return proof;
    }

    internal DemoOrganizationAuthorityProof RequireTransitionProof(Guid organization, string entityType, Guid entity, string eventType)
    {
        if (!workScope.OwnsOrganizationCommand(organization))
            throw new InvalidOperationException("Authority source requires its owning Organization command.");
        lock (_sync)
        {
            var expected = eventType == "ORGANIZATION_MEMBER_LEFT" ? "ORGANIZATION_MEMBER_REMOVED" : eventType;
            if (!_authorityProofs.TryGetValue((organization, entityType, entity), out var proof)
                || proof.CommandId != workScope.CommandId || proof.EventType != expected || proof.Version < 1 || proof.CreatedAt == default)
                throw new InvalidOperationException("Organization authority transition is unproven.");
            return proof;
        }
    }
}
