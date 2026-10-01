namespace StrataAI.Application.Identity;

public interface IIdentityRetryCleanupStore
{
    // One atomic pass removes at most 100 expired profile acknowledgments.
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}
