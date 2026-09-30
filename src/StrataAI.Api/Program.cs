using StrataAI.Application.Common;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/api/health", (IClock clock) => Results.Ok(new
{
    status = "ok",
    service = "strataai-api",
    timestamp = clock.UtcNow,
}));

app.MapGet("/api/runtime", (IHostEnvironment environment, IConfiguration configuration) =>
{
    var revision = configuration["STRATAAI_BUILD_REVISION"] ?? "development";
    var version = configuration["STRATAAI_BUILD_VERSION"] ?? "0.0.0-dev";

    return Results.Ok(new
    {
        service = "strataai-api",
        environment = environment.EnvironmentName,
        revision,
        version,
    });
});

app.MapHealthChecks("/healthz");

app.Run();

public partial class Program;
