using StrataAI.Application.Common;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddStrataAiRuntime(builder.Configuration);
builder.Services.AddHostedService<WorkerHeartbeat>();

var app = builder.Build();

app.MapGet("/healthz", (IClock clock) => Results.Ok(new
{
    status = "ok",
    service = "strataai-worker",
    timestamp = clock.UtcNow,
}));

app.MapGet(
    "/readyz",
    async (IRuntimeDependencyStatus status, CancellationToken cancellationToken) =>
    {
        var ready = await status.IsReadyAsync(cancellationToken);
        return ready
            ? Results.Ok(new { status = "ready", mode = status.Mode.ToString().ToLowerInvariant() })
            : Results.Json(
                new { status = "not-ready" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    });

app.MapGet("/runtime", (RuntimeDescriptor descriptor) => Results.Ok(new
{
    service = "strataai-worker",
    mode = descriptor.Mode.ToString().ToLowerInvariant(),
    revision = descriptor.BuildRevision,
    version = descriptor.BuildVersion,
}));

await app.RunAsync();

internal sealed class WorkerHeartbeat(
    ILogger<WorkerHeartbeat> logger,
    IClock clock,
    RuntimeDescriptor runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Component"] = "StrataAI.Worker",
            ["BuildRevision"] = runtime.BuildRevision,
            ["RuntimeMode"] = runtime.Mode.ToString(),
        });

        logger.LogInformation(
            "StrataAI Worker started at {Timestamp}.",
            clock.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
