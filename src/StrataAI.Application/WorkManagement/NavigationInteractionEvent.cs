using System.Collections.Frozen;

namespace StrataAI.Application.WorkManagement;

// PRD-01 personal navigation observations. Producers must authorize the actual
// target before creating a source; these are not shared Board mutation events.
public sealed record NavigationInteractionEvent
{
    private NavigationInteractionEvent(Guid eventId, Guid actorId, string eventType,
        Guid? organizationId, Guid? boardId, string entityType, Guid entityId,
        long version, DateTimeOffset createdAt)
    {
        if (eventId == Guid.Empty || actorId == Guid.Empty || entityId == Guid.Empty
            || version < 1 || createdAt == default || organizationId == Guid.Empty || boardId == Guid.Empty
            || (boardId.HasValue && !organizationId.HasValue))
            throw new ArgumentException("Invalid navigation observation identity, scope or revision.");
        EventId = eventId; ActorId = actorId; EventType = eventType;
        OrganizationId = organizationId; BoardId = boardId;
        EntityType = entityType; EntityId = entityId; Version = version;
        CreatedAt = new DateTimeOffset(createdAt.UtcTicks - createdAt.UtcTicks % 10, TimeSpan.Zero);
    }
    public Guid EventId { get; }
    public string EventType { get; }
    public Guid ActorId { get; }
    public Guid? OrganizationId { get; }
    public Guid? BoardId { get; }
    public string EntityType { get; }
    public Guid EntityId { get; }
    public long Version { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata => EmptyMetadata;
    private static readonly FrozenDictionary<string, string> EmptyMetadata = new Dictionary<string, string>().ToFrozenDictionary();

    public static NavigationInteractionEvent ApplicationContextChanged(Guid eventId, Guid actor,
        Guid? organization, DateTimeOffset at) => new(eventId, actor, "APPLICATION_CONTEXT_CHANGED",
            organization, null, organization.HasValue ? "Organization" : "ApplicationContext", organization ?? eventId, 1, at);
    public static NavigationInteractionEvent BoardOpened(Guid eventId, Guid actor, Guid organization,
        Guid board, long version, DateTimeOffset at) => new(eventId, actor, "BOARD_OPENED",
            organization, board, "Board", board, version, at);
    public static NavigationInteractionEvent CardOpened(Guid eventId, Guid actor, Guid organization,
        Guid board, Guid card, long version, DateTimeOffset at) => new(eventId, actor, "CARD_OPENED",
            organization, board, "Card", card, version, at);
}
