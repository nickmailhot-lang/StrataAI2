using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Routing;

namespace StrataAI.Api.Auth;

// ARCH-08-FR-006: aggregate endpoint responses, not credential/account facts.
public sealed class AuthenticationTelemetry(IMeterFactory factory)
{
    public const string MeterName = "StrataAI.Authentication";
    private Counter<long>? requests;
    private Histogram<double>? duration;

    internal static string? Operation(HttpContext context) => HttpMethods.IsPost(context.Request.Method)
        ? ((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText switch
        {
            "/auth/register" => "register", "/auth/login" => "login",
            "/auth/password/forgot" => "password_forgot", "/auth/password/reset" => "password_reset",
            "/auth/verification/resend" => "verification_resend", "/auth/verify-email" => "verify_email",
            "/auth/logout" => "logout", _ => null
        }) : null;

    public void Record(string operation, int status, string outcome, double seconds)
    {
        if (operation is not ("register" or "login" or "password_forgot" or "password_reset"
            or "verification_resend" or "verify_email" or "logout")) return;
        try
        {
            var meter = factory.Create(MeterName);
            KeyValuePair<string, object?>[] tags = [new("operation", operation),
                new("status_code", status is >= 100 and <= 599 ? status : 0),
                new("outcome", outcome is "completed" or "cancelled" ? outcome : "faulted")];
            requests ??= meter.CreateCounter<long>("strataai.authentication.requests", "{request}");
            try { requests.Add(1, tags); } catch { /* A listener cannot change the response. */ }
            duration ??= meter.CreateHistogram<double>("strataai.authentication.duration", "s");
            try { duration.Record(seconds, tags); } catch { /* Independent best-effort duration. */ }
        }
        catch { /* Optional diagnostics cannot change authoritative auth behavior. */ }
    }
}

public sealed class AuthenticationTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuthenticationTelemetry telemetry)
    {
        var operation = AuthenticationTelemetry.Operation(context);
        if (operation is null) { await next(context); return; }
        var started = Stopwatch.GetTimestamp(); var outcome = "faulted";
        try { await next(context); outcome = "completed"; }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        { outcome = "cancelled"; throw; }
        finally
        {
            telemetry.Record(operation, outcome == "completed" ? context.Response.StatusCode : 0,
                outcome, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}
