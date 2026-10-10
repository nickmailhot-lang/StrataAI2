using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;

namespace StrataAI.Infrastructure.WorkManagement;

// Normal Demo startup enables processing. Tests replace this DI option when
// controlling a manual lease, and separately exercise automatic hosted delivery.
public sealed record DemoCardReminderProcessingOptions(bool Enabled = true);

internal sealed class InMemoryCardReminderProcessing(InMemoryBackgroundJobStore jobs,
    CardReminderDeliveryHandler handler, ILogger<InMemoryCardReminderProcessing> logger) : IDemoCardReminderProcessing
{
    private readonly SemaphoreSlim _advance = new(1, 1);
    private readonly Guid _worker = Guid.NewGuid();
    private Guid? _after;

    public async Task<bool> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        await _advance.WaitAsync(cancellationToken);
        try
        {
            var organization = await jobs.NextDueReminderOrganizationAsync(_after, cancellationToken);
            if (organization is null) return false;
            _after = organization;
            var job = await jobs.ClaimReminderAsync(organization.Value, _worker, cancellationToken);
            if (job is null) return false;
            try
            {
                await handler.ExecuteAsync(job, cancellationToken);
                // A lost acknowledgment retains its committed effect. The next
                // actual lease recovers FIRED without duplicating publication.
                await jobs.CompleteAsync(job.OrganizationId, job.Id, job.LeaseId, _worker, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                await jobs.FailAsync(job.OrganizationId, job.Id, job.LeaseId, _worker,
                    "demo_reminder_delivery_failed", cancellationToken);
                logger.LogWarning("Demo reminder delivery failed. OrganizationId={OrganizationId}; JobId={JobId}; CorrelationId={CorrelationId}",
                    job.OrganizationId, job.Id, job.CorrelationId);
            }
            return true;
        }
        finally { _advance.Release(); }
    }
}

internal sealed class DemoCardReminderProcessingHost(IDemoCardReminderProcessing processing,
    DemoCardReminderProcessingOptions options, ILogger<DemoCardReminderProcessingHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await processing.AdvanceAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Demo reminder processing is unavailable."); }
            try { await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
