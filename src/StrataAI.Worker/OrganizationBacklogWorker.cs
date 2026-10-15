using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Worker;

// ARCH-08-FR-006: separate from job execution, with no claim, retry or dispatch.
internal sealed class OrganizationBacklogWorker : BackgroundService
{
    private readonly IOrganizationJobBacklogReader reader;
    private readonly Guid[] organizations;
    private OrganizationJobBacklog? snapshot;

    public OrganizationBacklogWorker(IOrganizationJobBacklogReader reader, OrganizationJobScope scope, IMeterFactory factory)
    {
        this.reader = reader;
        organizations = scope.OrganizationIds.Distinct().ToArray();
        if (organizations.Length is < 1 or > 100 || organizations.Contains(Guid.Empty))
            throw new ArgumentException("Backlog observation requires explicit valid Worker scope.", nameof(scope));
        try
        {
            var meter = factory.Create(WorkerJobTelemetry.MeterName);
            meter.CreateObservableGauge("strataai.worker.scoped.backlog", Observe, "{job}");
            meter.CreateObservableGauge("strataai.worker.scoped.backlog.available",
                () => new Measurement<long>(Volatile.Read(ref snapshot) is null ? 0 : 1,
                    new KeyValuePair<string, object?>("queue", "organization")));
        }
        catch { /* An optional diagnostic instrument cannot prevent startup. */ }
    }

    private IEnumerable<Measurement<long>> Observe()
    {
        var current = Volatile.Read(ref snapshot);
        return current is null ? [] :
        [new(current.Pending, new("queue", "organization"), new("state", "pending")),
         new(current.Running, new("queue", "organization"), new("state", "running"))];
    }

    internal async Task SampleAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            long pending = 0, running = 0;
            foreach (var organization in organizations)
            {
                var counts = await reader.ReadAsync(organization, deadline.Token);
                if (counts.Pending < 0 || counts.Running < 0) throw new InvalidOperationException("Invalid backlog counts.");
                pending = checked(pending + counts.Pending); running = checked(running + counts.Running);
            }
            deadline.Token.ThrowIfCancellationRequested();
            Volatile.Write(ref snapshot, new(pending, running));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch { Volatile.Write(ref snapshot, null); /* Unknown is not an empty queue. */ }
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
