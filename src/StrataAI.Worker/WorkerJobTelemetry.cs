using System.Diagnostics.Metrics;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Identity;

namespace StrataAI.Worker;

// ARCH-08-FR-006: outcomes of claimed attempts, never queue payloads or IDs.
internal sealed class WorkerJobTelemetry(IMeterFactory factory)
{
    public const string MeterName = "StrataAI.WorkerJobs";
    private Counter<long>? attempts;
    private Counter<long>? loopFailures;

    public void RecordOrganizationLoopFailure() => RecordLoopFailure("organization");
    public void RecordIdentityEmailLoopFailure() => RecordLoopFailure("identity_email");

    private void RecordLoopFailure(string queue)
    {
        try
        {
            loopFailures ??= factory.Create(MeterName).CreateCounter<long>("strataai.worker.loop.failures", "{failure}");
            loopFailures.Add(1, new KeyValuePair<string, object?>("queue", queue));
        }
        catch { /* Optional diagnostics cannot stop the next queue pass. */ }
    }

    public void Record(JobProcessingResult outcome)
    {
        if (outcome == JobProcessingResult.Empty) return;
        Record("organization", outcome switch
        {
            JobProcessingResult.Completed => "completed", JobProcessingResult.Retried => "retried",
            JobProcessingResult.LeaseLost => "lease_lost", _ => "unknown"
        });
    }

    public void Record(IdentityDeliveryPass outcome)
    {
        if (outcome == IdentityDeliveryPass.Empty) return;
        Record("identity_email", outcome switch
        {
            IdentityDeliveryPass.Sent => "sent", IdentityDeliveryPass.Cancelled => "cancelled",
            IdentityDeliveryPass.Retried => "retried", IdentityDeliveryPass.Failed => "failed",
            IdentityDeliveryPass.LeaseLost => "lease_lost", _ => "unknown"
        });
    }

    private void Record(string queue, string outcome)
    {
        try
        {
            attempts ??= factory.Create(MeterName).CreateCounter<long>("strataai.worker.jobs", "{attempt}");
            attempts.Add(1, new KeyValuePair<string, object?>("queue", queue), new KeyValuePair<string, object?>("outcome", outcome));
        }
        catch { /* Instrument/listener failures cannot affect committed work. */ }
    }
}
