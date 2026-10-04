namespace StrataAI.Application.WorkManagement;

// Internal audience/parent hint, never a feed response or authorization result.
// A Reminder's actor may be a different user changing the Card/container.
// Only its persisted personal owner is the private audience candidate.
public sealed record ActivityPrivateTarget(Guid OwnerId, string EntityType, Guid EntityId);

public interface IActivityPrivateTargetStore
{
    // Reads the actual source identity in the owning tenant transaction.
    // Inactive watch/cancelled Reminder records still interpret old events.
    Task<ActivityPrivateTarget?> FindAsync(Guid organizationId, Guid eventId, CancellationToken ct = default);
}
