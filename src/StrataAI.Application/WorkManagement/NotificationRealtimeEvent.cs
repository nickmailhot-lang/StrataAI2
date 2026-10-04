using System.Collections.Frozen;
using System.Globalization;

namespace StrataAI.Application.WorkManagement;

// Recipient-private journal envelope. Never append to a Board replay stream.
// Admission and persistence are separate requirements; this factory grants neither.
public sealed record NotificationRealtimeEvent
{
    private NotificationRealtimeEvent() { }
    public Guid EventId { get; private init; }
    public string EventType { get; private init; } = "";
    public Guid ActorId { get; private init; }
    public Guid RecipientId { get; private init; }
    public Guid OrganizationId { get; private init; }
    public Guid BoardId { get; private init; }
    public string EntityType => "Notification";
    public Guid EntityId { get; private init; }
    public long Version { get; private init; }
    public string Sequence { get; private init; } = "";
    public DateTimeOffset CreatedAt { get; private init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; } = FrozenDictionary<string, object?>.Empty;

    public static NotificationRealtimeEvent Created(CardNotification notification, Guid eventId, long sequence)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return Build(notification, eventId, sequence, "NOTIFICATION_CREATED", notification.ActorId, 1, notification.CreatedAt);
    }

    public static NotificationRealtimeEvent Read(CardNotification notification, Guid eventId, long sequence, DateTimeOffset readAt)
    {
        ArgumentNullException.ThrowIfNull(notification);
        // A retry must carry the persisted first read timestamp, never its retry clock.
        if (notification.ReadAt is not { } first || first != readAt || readAt < notification.CreatedAt)
            throw new ArgumentException("A persisted notification read is required.", nameof(readAt));
        return Build(notification, eventId, sequence, "NOTIFICATION_READ", notification.RecipientId, 2, readAt);
    }

    private static NotificationRealtimeEvent Build(CardNotification n, Guid eventId, long sequence,
        string type, Guid actor, long version, DateTimeOffset created)
    {
        ArgumentNullException.ThrowIfNull(n);
        if (eventId == Guid.Empty || sequence < 1 || n.Id == Guid.Empty || n.OrganizationId == Guid.Empty ||
            n.BoardId == Guid.Empty || n.CardId == Guid.Empty || n.EventId == Guid.Empty || n.ActorId == Guid.Empty ||
            n.RecipientId == Guid.Empty || n.CardVersion < 1)
            throw new ArgumentException("Invalid notification event scope.");
        return new() { EventId = eventId, EventType = type, ActorId = actor, RecipientId = n.RecipientId,
            OrganizationId = n.OrganizationId, BoardId = n.BoardId, EntityId = n.Id, Version = version,
            Sequence = sequence.ToString(CultureInfo.InvariantCulture), CreatedAt = created.ToUniversalTime() };
    }
}
