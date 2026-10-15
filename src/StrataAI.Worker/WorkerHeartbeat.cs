using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StrataAI.Application.Common;
using StrataAI.Application.Runtime;
using StrataAI.Worker;

internal sealed class WorkerHeartbeat(
    ILogger<WorkerHeartbeat> logger,
    IClock clock,
    RuntimeDescriptor runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = WorkerLog.BeginScope(logger, new Dictionary<string, object?>
        {
            ["Component"] = "StrataAI.Worker",
            ["BuildRevision"] = runtime.BuildRevision,
            ["RuntimeMode"] = runtime.Mode.ToString(),
        });

        WorkerLog.Information(logger,
            "StrataAI Worker started at {Timestamp}.",
            clock.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
