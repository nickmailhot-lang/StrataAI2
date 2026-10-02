namespace StrataAI.Application.WorkManagement;

// Internal storage contract. These records must never be disclosed before fresh
// recipient, Organization and entity authorization by the notification service.
public sealed record CardNotification(Guid Id, Guid OrganizationId, Guid BoardId,
    Guid CardId, Guid EventId, Guid RecipientId, Guid ActorId, long CardVersion,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)
{
    public const string Type = "CARD_ASSIGNED";
    public string NotificationType { get; init; } = Type;

    public static CardNotification? FromActivity(WorkEvent change, Guid recipientId)
    {
        if (!CardWatchActivity.IsRelevant(change)) throw new ArgumentException("Unconfigured Card watch activity.");
        var item = From(change with { EventType = "CARD_MEMBER_ADDED" }, recipientId);
        return item is null ? null : item with { NotificationType = change.EventType };
    }

    public static CardNotification? From(WorkEvent change, Guid recipientId)
    {
        if (recipientId == Guid.Empty || change.EventId == Guid.Empty || change.OrganizationId == Guid.Empty ||
            change.BoardId == Guid.Empty || change.ActorId == Guid.Empty || change.EntityId == Guid.Empty ||
            change.Version < 1 || change.EventType != "CARD_MEMBER_ADDED" || change.EntityType != "Card")
            throw new ArgumentException("Invalid assignment notification scope.");
        return recipientId == change.ActorId ? null : new(Guid.NewGuid(), change.OrganizationId,
            change.BoardId, change.EntityId, change.EventId, recipientId, change.ActorId, change.Version,
            change.CreatedAt, null);
    }
}

public interface IWorkNotificationStore
{
    // Persist with the originating Card/event/receipt transaction. Replay keeps
    // the first event-recipient notification and self-actions are suppressed.
    Task AppendCardAssignmentAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default);
    Task AppendCardActivityAsync(WorkEvent change, Guid recipientId, CancellationToken cancellationToken = default);

    // Internal bounded storage window, not an authorized inbox response.
    Task<IReadOnlyList<CardNotification>> ListCardNotificationsAsync(Guid organizationId,
        Guid recipientId, Guid? after = null, CancellationToken cancellationToken = default);
}
