using StrataAI.Api;
using StrataAI.Application.Common;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
var runtime = builder.Services.AddStrataAiRuntime(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    service = "strataai-api",
}));

app.MapGet("/api/health", (IClock clock) => Results.Ok(new
{
    status = "ok",
    service = "strataai-api",
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

app.MapGet("/api/runtime", (RuntimeDescriptor descriptor) => Results.Ok(new
{
    service = "strataai-api",
    mode = descriptor.Mode.ToString().ToLowerInvariant(),
    revision = descriptor.BuildRevision,
    version = descriptor.BuildVersion,
}));

if (runtime.Mode == RuntimeMode.Demo)
{
    var demo = app.MapGroup("/api/demo");

    demo.MapGet("/state", (IDemoDataStore dataStore) => Results.Ok(dataStore.GetState()));
    demo.MapPost("/reset", (IDemoDataStore dataStore) => Results.Ok(dataStore.Reset()));
    demo.MapDelete("/state", (IDemoDataStore dataStore) => Results.Ok(dataStore.Clear()));
}

app.Run();

public partial class Program;
