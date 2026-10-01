namespace StrataAI.Application.Identity;

public interface IIdentityRetryCleanupStore
{
    // One atomic pass removes at most 100 expired records from each profile, revocation and sign-in retry table.
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}
