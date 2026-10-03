using StrataAI.Application.BackgroundJobs;
using System.Diagnostics;

namespace StrataAI.Worker;

internal sealed class OrganizationJobWorker(
    BackgroundJobProcessor processor,
    OrganizationJobScope scope,
    ILogger<OrganizationJobWorker> logger) : BackgroundService
{
    private readonly Guid _workerId = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var organizationId in scope.OrganizationIds)
            {
                try
                {
                    // Drain ready work without imposing a one-second delay per
                    // event. Count and elapsed-time bounds retain round-robin
                    // fairness; each job still claims and acknowledges its own
                    // lease, and failure/retry backoff remains in the job store.
                    var started = Stopwatch.GetTimestamp();
                    for (var processed = 0; processed < 32; processed++)
                    {
                        var outcome = await processor.ProcessOneAsync(organizationId, _workerId, stoppingToken);
                        if (outcome == JobProcessingResult.Empty) break;
                        logger.LogInformation("Job pass {Outcome} for Organization {OrganizationId}, Worker {WorkerId}.", outcome, organizationId, _workerId);
                        if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(250)) break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    // No exception message: SQL/provider diagnostics can include
                    // secrets. The committed lease is recovered after expiry.
                    logger.LogWarning("Job pass failed for Organization {OrganizationId}, Worker {WorkerId}.", organizationId, _workerId);
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}

internal sealed record OrganizationJobScope(IReadOnlyList<Guid> OrganizationIds);

internal sealed class BackgroundJobDiagnostics(ILogger<BackgroundJobDiagnostics> logger) : IBackgroundJobDiagnostics
{
    public void Record(ClaimedBackgroundJob job, JobProcessingResult outcome) =>
        logger.LogInformation("Job {JobId} {JobType} pass {Outcome}, Organization {OrganizationId}, Actor {ActorId}, Service {ServiceIdentity}, Worker {WorkerId}, Attempt {AttemptCount}, CorrelationId {CorrelationId}.",
            job.Id, job.JobType, outcome, job.OrganizationId, job.ActorId, job.ServiceIdentity, job.WorkerId, job.AttemptCount, job.CorrelationId);
}
