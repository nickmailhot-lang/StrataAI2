using System.Diagnostics;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;

namespace StrataAI.Worker;

// A separate production routing loop, sharing the normal leased dispatcher.
// Each discovered Organization still executes through forced tenant RLS.
internal sealed class OrganizationDeletionDiscoveryWorker(IOrganizationDeletionScopeReader scopes,
    BackgroundJobProcessor processor, ILogger<OrganizationDeletionDiscoveryWorker> logger) : BackgroundService
{
    private readonly Guid _workerId = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? after = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var page = await scopes.ReadAsync(after, 100, stoppingToken);
                foreach (var organization in page)
                {
                    try
                    {
                        var started = Stopwatch.GetTimestamp();
                        for (var processed = 0; processed < 32; processed++)
                        {
                            if (await processor.ProcessOneAsync(organization, _workerId, stoppingToken) == JobProcessingResult.Empty) break;
                            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(250)) break;
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception) { logger.LogWarning("Discovered deletion pass failed for Organization {OrganizationId}; other scopes continue.", organization); }
                }
                // Seek through the whole eligible set, then wrap so newly
                // queued lower UUIDs and delayed/crashed claims are revisited.
                after = page.Count == 100 ? page[^1] : null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                logger.LogWarning("Organization deletion discovery pass failed; queued work remains recoverable.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
