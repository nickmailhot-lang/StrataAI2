using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrataAI.Application.Common;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHostedService<WorkerHeartbeat>();

await builder.Build().RunAsync();

internal sealed class WorkerHeartbeat(
    ILogger<WorkerHeartbeat> logger,
    IClock clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "StrataAI Worker started at {Timestamp}. Revision={Revision}",
            clock.UtcNow,
            Environment.GetEnvironmentVariable("STRATAAI_BUILD_REVISION") ?? "development");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
