using System.Diagnostics;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;
using StrataAI.Application.Common;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.BackgroundJobs;

namespace StrataAI.Worker;

// A separate production routing loop, using a dispatcher restricted to operational lifecycle leases.
// Each discovered Organization still executes through forced tenant RLS.
internal sealed class OrganizationOperationalLifecycleDiscoveryWorker(IOrganizationOperationalLifecycleScopeReader scopes,
    PostgresConnectionFactory connections, IClock clock, IOrganizationOperationalLifecycleDeliveryStore delivery, IBackgroundJobDiagnostics diagnostics, ILogger<OrganizationOperationalLifecycleDiscoveryWorker> logger) : BackgroundService
{
    private readonly BackgroundJobProcessor _processor = new(new PostgresBackgroundJobStore(connections, operationalLifecycleJobsOnly: true),
        clock, [new OrganizationOperationalLifecycleDeliveryHandler(delivery)], diagnostics);
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
                            if (await _processor.ProcessOneAsync(organization, _workerId, stoppingToken) == JobProcessingResult.Empty) break;
                            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(250)) break;
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception) { WorkerLog.Warning(logger, "Discovered operational lifecycle pass failed for Organization {OrganizationId}; other scopes continue.", organization); }
                }
                // Seek through the whole eligible set, then wrap so newly
                // queued lower UUIDs and delayed/crashed claims are revisited.
                after = page.Count == 100 ? page[^1] : null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                WorkerLog.Warning(logger, "Organization operational lifecycle discovery pass failed; queued work remains recoverable.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
