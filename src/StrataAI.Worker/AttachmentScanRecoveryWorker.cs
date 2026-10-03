using StrataAI.Application.WorkManagement;

namespace StrataAI.Worker;

internal sealed class AttachmentScanRecoveryWorker(
    IAttachmentScanRecoveryStore store,
    OrganizationJobScope scope,
    ILogger<AttachmentScanRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var index=0;
        while(!stoppingToken.IsCancellationRequested)
        {
            var organizationId=scope.OrganizationIds[index];
            index=(index+1)%scope.OrganizationIds.Count;
            try
            {
                var result=await store.RecoverPageAsync(organizationId,32,stoppingToken);
                if(result.Recovered>0)logger.LogInformation("Scan recovery completed {Count} failures for Organization {OrganizationId}.",
                    result.Recovered,organizationId);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception)
            {
                logger.LogWarning("Scan recovery unavailable for Organization {OrganizationId}.",organizationId);
            }
            try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
        }
    }
}
