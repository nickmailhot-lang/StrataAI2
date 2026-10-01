using StrataAI.Application.Identity;

namespace StrataAI.Worker;

internal sealed class IdentityRetryCleanupWorker(IIdentityRetryCleanupStore store, ILogger<IdentityRetryCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = Guid.NewGuid();
            try
            {
                var removed = await store.PurgeExpiredAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Removed {Count} expired identity profile retry acknowledgments. CorrelationId {CorrelationId}.", removed, correlationId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Identity retry cleanup failed; the next pass will retry. CorrelationId {CorrelationId}.", correlationId); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
