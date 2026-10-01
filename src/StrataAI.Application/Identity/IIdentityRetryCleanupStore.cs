namespace StrataAI.Application.Identity;

public interface IIdentityRetryCleanupStore
{
    // One atomic pass removes at most 100 expired records from each of the five identity retry tables.
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}
