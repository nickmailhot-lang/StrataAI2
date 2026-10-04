namespace StrataAI.Application.WorkManagement;

// Actor-private invalidation. Never append to shared Board activity or expose
// another user's preference through a Board-wide subscription.
public sealed record BoardStarEvent(Guid EventId, Guid ActorId, Guid OrganizationId,
    Guid BoardId, Guid EntityId, long Version, DateTimeOffset CreatedAt)
{
    public string EventType => "BOARD_STARRED";
    public string EntityType => "UserBoardPreference";
    public IReadOnlyDictionary<string, object> Metadata { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(new Dictionary<string, object>());
}
