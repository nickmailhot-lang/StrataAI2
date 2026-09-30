using StrataAI.Application.Common;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<WorkerHeartbeat>();

var app = builder.Build();

app.MapGet("/healthz", (IClock clock) => Results.Ok(new
{
    status = "ok",
    service = "strataai-worker",
    timestamp = clock.UtcNow,
}));

app.MapGet("/runtime", (IHostEnvironment environment, IConfiguration configuration) =>
{
    var revision = configuration["STRATAAI_BUILD_REVISION"] ?? "development";
    var version = configuration["STRATAAI_BUILD_VERSION"] ?? "0.0.0-dev";

    return Results.Ok(new
    {
        service = "strataai-worker",
        environment = environment.EnvironmentName,
        revision,
        version,
    });
});

app.MapHealthChecks("/health");

await app.RunAsync();

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
