using StrataAI.Application.Onboarding;

namespace StrataAI.Worker;

internal sealed class InvitationIssuerAuthorityWorker(IInvitationIssuerAuthorityDeliveryStore store,
    ILogger<InvitationIssuerAuthorityWorker> logger) : BackgroundService
{
    private readonly Guid _workerId = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claim = await store.ClaimAsync(_workerId, stoppingToken);
                if (claim is not null && !await store.DeliverAsync(claim, 100, stoppingToken))
                    logger.LogWarning("Invitation issuer authority lease unavailable; queued work remains recoverable.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // No account, recipient, source payload or private routing fields.
                logger.LogWarning("Invitation issuer authority delivery unavailable; queued work remains recoverable.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
