using StrataAI.Application.WorkManagement;

namespace StrataAI.Worker;

internal sealed class AttachmentPreviewBackfillWorker(
    IAttachmentPreviewBackfillStore store,
    OrganizationJobScope scope,
    ILogger<AttachmentPreviewBackfillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One Organization per tick; bounded pages and round-robin scope avoid
        // a large tenant or a failed source monopolizing the separate Worker.
        var index = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var organizationId = scope.OrganizationIds[index];
            index = (index + 1) % scope.OrganizationIds.Count;
            try
            {
                var result = await store.EnqueuePageAsync(organizationId, 32, stoppingToken);
                if (result.Enqueued > 0)
                    logger.LogInformation("Preview maintenance enqueued {Count} jobs for Organization {OrganizationId}.",
                        result.Enqueued, organizationId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // Provider/SQL diagnostics must not disclose private metadata.
                // An aborted transaction retains the prior cursor for retry.
                logger.LogWarning("Preview maintenance unavailable for Organization {OrganizationId}.", organizationId);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
