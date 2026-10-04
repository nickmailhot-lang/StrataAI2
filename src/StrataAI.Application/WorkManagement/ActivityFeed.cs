namespace StrataAI.Application.WorkManagement;

public enum ActivityTargetKind { Board, Card }
public sealed record ActivityCursor(DateTimeOffset CreatedAt, Guid EventId);
public sealed record ActivityCursorBinding(Guid OrganizationId, Guid ViewerId, ActivityTargetKind Kind, Guid TargetId);
public interface IActivityCursorCodec
{
    string Encode(ActivityCursorBinding binding, ActivityCursor position);
    bool TryDecode(ActivityCursorBinding binding, string token, out ActivityCursor? position);
}
public interface IActivityFeedStore
{
    // Visibility (including personal ownership) filters BEFORE the 51-row
    // bound. This is an internal candidate window; consumers re-admit after
    // source/current parent waits and verify the issuing session.
    Task<IReadOnlyList<ActivityEventSource>> ReadAsync(ActivityCursorBinding binding, ActivityCursor? before,
        CancellationToken ct = default);
}
public sealed record ActivityItem(Guid EventId, Guid OrganizationId, Guid BoardId, Guid ActorId,
    string ActorLabel, string EventType, string EntityType, Guid EntityId, string Version,
    DateTimeOffset CreatedAt, IReadOnlyDictionary<string, object?> Metadata, Guid CurrentBoardId);
public sealed record ActivityPage(Guid OrganizationId, ActivityTargetKind Kind, Guid TargetId,
    IReadOnlyList<ActivityItem> Items, string? NextCursor);
