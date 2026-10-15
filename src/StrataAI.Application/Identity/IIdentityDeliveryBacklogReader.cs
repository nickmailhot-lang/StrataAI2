namespace StrataAI.Application.Identity;

// Persisted state counts, not eligibility or distinct delivered messages.
public sealed record IdentityDeliveryBacklog(long Pending, long Running);

public interface IIdentityDeliveryBacklogReader
{
    Task<IdentityDeliveryBacklog> ReadAsync(CancellationToken cancellationToken);
}
