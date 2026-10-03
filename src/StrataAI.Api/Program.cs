using StrataAI.Api;
using StrataAI.Api.Auth;
using StrataAI.Api.Organizations;
using StrataAI.Api.Onboarding;
using StrataAI.Api.WorkManagement;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Runtime;
using StrataAI.Infrastructure.WorkManagement;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMetrics();
builder.Services.AddStrataAiOperatorMetrics(builder.Configuration, typeof(Program).Assembly);
builder.Services.AddSingleton<BoardSharingTelemetry>();
builder.Services.AddSingleton<ChecklistClientTelemetry>();
// Transport connection tokens appear in request query strings. Retain warnings
// without logging request-start URLs at the default Information level.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter<StrataAI.Application.Onboarding.InvitationSurface>(
            System.Text.Json.JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter<StrataAI.Application.Identity.AccountStatus>(
            System.Text.Json.JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter<StrataAI.Application.WorkManagement.BoardVisibility>(
            System.Text.Json.JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter<StrataAI.Application.WorkManagement.BoardRole>(
            System.Text.Json.JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter<StrataAI.Application.WorkManagement.BoardLifecycleState>(
            System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter<StrataAI.Application.WorkManagement.WorkItemLifecycleState>(
            System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICommandActorContext, HttpCommandActorContext>();
builder.Services.AddSingleton<IIdentityCommandContext, HttpIdentityCommandContext>();
builder.Services.AddSingleton<ICommandActorAuthorization, CommandActorAuthorization>();
builder.Services.AddSingleton<StrataAI.Application.WorkManagement.IWorkCommandContext, HttpWorkCommandContext>();
builder.Services
    .AddAuthentication(SessionAuthenticationDefaults.Scheme)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSecurityRateLimits(builder.Configuration);

var runtime = builder.Services.AddStrataAiRuntime(builder.Configuration, typeof(Program).Assembly);
builder.Services.AddStrataAiIdentity(builder.Configuration, runtime);
builder.Services.AddStrataAiOrganizations(runtime);
builder.Services.AddStrataAiOnboarding(runtime, builder.Configuration);
builder.Services.AddStrataAiWorkManagement(runtime);
builder.Services.AddSingleton(new WorkRealtimeOrigin(builder.Configuration));
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 4096;
    options.StreamBufferCapacity = 1;
    options.MaximumParallelInvocationsPerClient = 1;
}).AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter<AccountStatus>(
            System.Text.Json.JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<BoardSharingTelemetryMiddleware>();
app.UseMiddleware<RuntimeDatabaseSecurityMiddleware>();
app.UseRouting();
app.UseMiddleware<WorkRealtimeOriginMiddleware>();
app.UseMiddleware<CsrfProtectionMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<WorkIdempotencyMiddleware>();
app.UseMiddleware<IdentityProfileIdempotencyMiddleware>();

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
            ? Results.Ok(new
            {
                status = "ready",
                mode = status.Mode.ToString().ToLowerInvariant(),
            })
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

    demo.MapGet(
        "/state",
        (IDemoDataStore dataStore) => Results.Ok(dataStore.GetState()));
    demo.MapPost(
        "/reset",
        (IDemoDataStore dataStore) => Results.Ok(dataStore.Reset()));
    demo.MapDelete(
        "/state",
        (IDemoDataStore dataStore) => Results.Ok(dataStore.Clear()));
}

app.MapIdentityEndpoints(
    runtime,
    app.Services.GetRequiredService<IdentityPolicy>());
app.MapOrganizationEndpoints();
app.MapInvitationEndpoints(runtime);
app.MapWorkManagementEndpoints();
app.MapWorkSynchronizationEndpoints();
app.MapChecklistClientTelemetry();
app.MapHub<WorkRealtimeHub>("/boards/live", options =>
{
    options.ApplicationMaxBufferSize = 131072;
    options.TransportMaxBufferSize = 4096;
});
app.MapHub<IdentityRealtimeHub>("/me/live", options =>
{
    options.ApplicationMaxBufferSize = 131072;
    options.TransportMaxBufferSize = 4096;
}).RequireAuthorization();

app.Run();

public partial class Program;
