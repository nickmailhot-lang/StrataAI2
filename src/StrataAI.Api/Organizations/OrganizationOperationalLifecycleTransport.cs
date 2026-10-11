using StrataAI.Application.Organizations;

namespace StrataAI.Api.Organizations;

public sealed record OrganizationOperationalLifecycleWireEvent(Guid EventId, Guid OrganizationId, Guid ActorId,
    string EventType, string EntityType, Guid EntityId, long Version, string PreviousStatus, string Status,
    string ActingCapacity, DateTimeOffset CreatedAt);
public sealed record OrganizationOperationalLifecycleWirePage(Guid OrganizationId, Guid ActorId, string Status,
    long Version, long AfterVersion, long? NextVersion, bool Pending, IReadOnlyList<OrganizationOperationalLifecycleWireEvent> Events);
public static class OrganizationOperationalLifecycleTransport
{
    public static OrganizationOperationalLifecycleWirePage Page(OrganizationOperationalLifecycleReplayPage source) => new(
        source.OrganizationId, source.ActorId, State(source.Status), source.Version, source.AfterVersion,
        source.NextVersion, source.Pending, source.Events.Select(item => new OrganizationOperationalLifecycleWireEvent(
            item.EventId, item.OrganizationId, item.ActorId, item.EventType, item.EntityType, item.EntityId, item.Version,
            State(item.PreviousStatus), State(item.Status), item.ActingCapacity, item.CreatedAt)).ToArray());
    private static string State(OrganizationStatus status) => status switch {
        OrganizationStatus.Active => "ACTIVE", OrganizationStatus.Suspended => "SUSPENDED", OrganizationStatus.Deactivated => "DEACTIVATED",
        _ => throw new InvalidOperationException("Invalid operational lifecycle transport state."),
    };
}
