using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using StrataAI.Application.Identity;

namespace StrataAI.Worker;

// ARCH-08-FR-006: observation cannot claim, send, acknowledge or retry mail.
internal sealed class IdentityEmailBacklogWorker : BackgroundService
{
    private readonly IIdentityDeliveryBacklogReader reader;
    private IdentityDeliveryBacklog? snapshot;

    public IdentityEmailBacklogWorker(IIdentityDeliveryBacklogReader reader, IMeterFactory factory)
    {
        this.reader = reader;
        try
        {
            var meter = factory.Create(WorkerJobTelemetry.MeterName);
            meter.CreateObservableGauge("strataai.worker.identity.backlog", Observe, "{job}");
            meter.CreateObservableGauge("strataai.worker.identity.backlog.available",
                () => new Measurement<long>(Volatile.Read(ref snapshot) is null ? 0 : 1,
                    new KeyValuePair<string, object?>("queue", "identity_email")));
        }
        catch { /* Optional diagnostics cannot prevent the mail host starting. */ }
    }

    private IEnumerable<Measurement<long>> Observe()
    {
        var current = Volatile.Read(ref snapshot);
        return current is null ? [] :
        [new(current.Pending, new("queue", "identity_email"), new("state", "pending")),
         new(current.Running, new("queue", "identity_email"), new("state", "running"))];
    }

    internal async Task SampleAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var counts = await reader.ReadAsync(deadline.Token);
            if (counts.Pending < 0 || counts.Running < 0) throw new InvalidOperationException("Invalid identity backlog counts.");
            deadline.Token.ThrowIfCancellationRequested();
            Volatile.Write(ref snapshot, counts);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch { Volatile.Write(ref snapshot, null); /* Unavailable is not zero. */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SampleAsync(stoppingToken);
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
