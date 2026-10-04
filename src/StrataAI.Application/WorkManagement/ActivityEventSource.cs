using System.Collections.ObjectModel;

namespace StrataAI.Application.WorkManagement;

// Internal immutable journal projection. A source window is not an authorized
// activity feed: the owning consumer must admit every historical/current Board
// and entity, including the private Watch/Reminder audience, before disclosure.
public sealed record ActivityEventSource(Guid EventId, Guid OrganizationId, Guid BoardId, Guid ActorId,
    string ActorLabel, string EventType, string EntityType, Guid EntityId, long Version, DateTimeOffset CreatedAt)
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyMetadata =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    public IReadOnlyDictionary<string, object?> Metadata => EmptyMetadata;
}

public interface IActivityEventSourceStore
{
    // Newest-first stable timestamp/ID seek, including committed sources whose
    // realtime delivery is not ready. At most 51 rows; no offset/full-journal scan.
    Task<IReadOnlyList<ActivityEventSource>> ReadBoardWindowAsync(Guid organizationId, Guid boardId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeEventId, CancellationToken ct = default);

    // One explicit historical Board slice. A complete authorized Card feed
    // must combine every eligible source Board; this is not current-Board-only history.
    Task<IReadOnlyList<ActivityEventSource>> ReadCardWindowAsync(Guid organizationId, Guid sourceBoardId, Guid cardId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeEventId, CancellationToken ct = default);
}

public static class ActivityEventSourceWindow
{
    public const int MaximumRows = 51;
    public static void RequireCursor(DateTimeOffset? createdAt, Guid? id)
    {
        if (createdAt.HasValue != id.HasValue || id == Guid.Empty
            || createdAt is { } at && (at.Offset != TimeSpan.Zero || at.Ticks % 10 != 0))
            throw new ArgumentException("Activity cursor requires a UTC microsecond timestamp and stable event identity.");
    }
}
