namespace StrataAI.Application.WorkManagement;

// Internal event-time scope, never an authorized recipient list or API response.
// The producer must capture this after the Card mutation in its owning transaction.
public sealed record CardWatchActivity(Guid OrganizationId, Guid BoardId, Guid ListId, Guid CardId)
{
    public static bool IsRelevant(WorkEvent change) => change.EntityType == "Card" && change.EventType is
        "CARD_CREATED" or "CARD_UPDATED" or "CARD_MOVED" or "CARD_ARCHIVED" or "CARD_RESTORED" or
        "CARD_MEMBER_ADDED" or "CARD_MEMBER_REMOVED" or "LABEL_ADDED" or "LABEL_REMOVED";

    public static CardWatchActivity? Capture(WorkEvent change, CardRecord current)
    {
        if (!IsRelevant(change)) return null;
        if (change.EventId == Guid.Empty || change.ActorId == Guid.Empty || current.OrganizationId == Guid.Empty ||
            current.BoardId == Guid.Empty || current.ListId == Guid.Empty || current.Id == Guid.Empty ||
            change.OrganizationId != current.OrganizationId || change.BoardId != current.BoardId ||
            change.EntityId != current.Id || change.Version != current.Version || current.Version < 1 ||
            current.LifecycleState != (change.EventType == "CARD_ARCHIVED"
                ? WorkItemLifecycleState.Archived : WorkItemLifecycleState.Active))
            throw new ArgumentException("Card watch activity must match the triggering Card revision.");
        return new(current.OrganizationId, current.BoardId, current.ListId, current.Id);
    }

    public bool Matches(WatchSubscription subscription) => subscription.OrganizationId == OrganizationId &&
        subscription.Watching && (subscription.EntityType switch
        {
            "CARD" => subscription.EntityId == CardId,
            "LIST" => subscription.EntityId == ListId,
            "BOARD" => subscription.EntityId == BoardId,
            _ => false,
        });
}
