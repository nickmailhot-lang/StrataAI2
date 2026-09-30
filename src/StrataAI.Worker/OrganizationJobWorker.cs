using StrataAI.Application.BackgroundJobs;

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
                    var outcome = await processor.ProcessOneAsync(organizationId, _workerId, stoppingToken);
                    if (outcome != JobProcessingResult.Empty)
                        logger.LogInformation("Job pass {Outcome} for Organization {OrganizationId}, Worker {WorkerId}.", outcome, organizationId, _workerId);
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
