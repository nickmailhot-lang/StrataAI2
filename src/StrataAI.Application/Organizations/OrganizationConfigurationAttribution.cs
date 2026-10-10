namespace StrataAI.Application.Organizations;

// Captured with the source command, never reconstructed from current accounts
// or memberships when historical Activity is read.
public sealed record OrganizationConfigurationAttribution(Guid OrganizationId, Guid EventId,
    Guid ActorId, long Version, string ActorLabel, string ActingCapacity,
    Guid? SourceMembershipId, long? SourceMembershipVersion)
{
    public bool Valid => OrganizationId != Guid.Empty && EventId != Guid.Empty && ActorId != Guid.Empty
        && Version > 0 && !string.IsNullOrWhiteSpace(ActorLabel) && ActorLabel.Length <= 160
        && !ActorLabel.Any(char.IsControl)
        && (ActingCapacity == "UNRECORDED" && SourceMembershipId is null && SourceMembershipVersion is null
            || ActingCapacity is "ORGANIZATION_OWNER" or "ORGANIZATION_ADMIN"
                && SourceMembershipId is { } member && member != Guid.Empty && SourceMembershipVersion is > 0);
}

public interface IOrganizationConfigurationAttributionReader
{
    // At most one bounded history window, within its owning tenant transaction.
    Task<IReadOnlyList<OrganizationConfigurationAttribution>> ReadAttributionAsync(Guid organization,
        IReadOnlyList<Guid> eventIds, CancellationToken ct);
}
