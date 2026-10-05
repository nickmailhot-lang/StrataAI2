using System.Collections.Frozen;

namespace StrataAI.Application.WorkManagement;

// Canonical personal interaction sources, separate from shared Work history
// and optional analytics. The authenticated producer owns ActorId. Criteria,
// snippets, result identities and content hashes are never event metadata.
public sealed record SearchInteractionEvent
{
    private SearchInteractionEvent(Guid eventId, Guid actorId, Guid? organizationId, Guid? boardId,
        string eventType, DateTimeOffset createdAt)
    {
        if (eventId == Guid.Empty || actorId == Guid.Empty || createdAt == default)
            throw new ArgumentException("Invalid search interaction identity or clock.");
        if ((organizationId is null) != (boardId is null) || organizationId == Guid.Empty || boardId == Guid.Empty)
            throw new ArgumentException("Invalid search interaction Board scope.");
        EventId = eventId; ActorId = actorId; OrganizationId = organizationId; BoardId = boardId;
        EventType = eventType;
        CreatedAt = new DateTimeOffset(createdAt.UtcTicks - createdAt.UtcTicks % 10, TimeSpan.Zero);
    }

    public Guid EventId { get; }
    public string EventType { get; }
    public Guid ActorId { get; }
    public Guid? OrganizationId { get; }
    public Guid? BoardId { get; }
    // Each source represents one immutable execution/change observation. It
    // does not increment the Board, Card, profile or session preference version.
    public string EntityType => EventType == "SEARCH_EXECUTED" ? "Search" : "BoardFilter";
    public Guid EntityId => EventId;
    public long Version => 1;
    public IReadOnlyDictionary<string, string> Metadata => EmptyMetadata;
    public DateTimeOffset CreatedAt { get; }
    private static readonly FrozenDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>().ToFrozenDictionary();

    public static SearchInteractionEvent SearchExecuted(Guid eventId, Guid actorId, DateTimeOffset createdAt) =>
        new(eventId, actorId, null, null, "SEARCH_EXECUTED", createdAt);

    public static SearchInteractionEvent BoardFilterChanged(Guid eventId, Guid actorId, Guid organizationId,
        Guid boardId, DateTimeOffset createdAt) =>
        new(eventId, actorId, organizationId, boardId, "BOARD_FILTER_CHANGED", createdAt);
}

// Implementations must use private actor admission, append original identity
// once, and enforce current Board admission for Board-scoped disclosure. This
// contract is not a producer registration or public replay endpoint.
public interface ISearchInteractionEventStore
{
    Task AppendAsync(SearchInteractionEvent source, CancellationToken cancellationToken = default);
}
