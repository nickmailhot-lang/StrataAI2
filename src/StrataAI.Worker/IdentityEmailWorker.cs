using StrataAI.Application.Identity;

namespace StrataAI.Worker;

internal sealed class IdentityEmailWorker(IdentityDeliveryProcessor processor,ILogger<IdentityEmailWorker> logger) : BackgroundService
{
    private readonly Guid _workerId=Guid.NewGuid();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await processor.ProcessOneAsync(_workerId,stoppingToken); }
            catch (OperationCanceledException) when(stoppingToken.IsCancellationRequested) { return; }
            catch(Exception) { logger.LogWarning("Identity delivery pass failed. Worker {WorkerId}.",_workerId); }
            try { await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { return; }
        }
    }
}

internal sealed class IdentityDeliveryDiagnostics(ILogger<IdentityDeliveryDiagnostics> logger) : IIdentityDeliveryDiagnostics
{
    public void Record(IdentityDeliveryJob job,IdentityDeliveryPass outcome) =>
        logger.LogInformation("Identity job {JobId} {Purpose} {Outcome}, Subject {UserId}, Worker {WorkerId}, Attempt {Attempt}, CorrelationId {CorrelationId}.",
            job.Id,job.Purpose,outcome,job.UserId,job.WorkerId,job.AttemptCount,job.CorrelationId);
}
