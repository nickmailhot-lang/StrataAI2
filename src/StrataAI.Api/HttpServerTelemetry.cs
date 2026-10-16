using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StrataAI.Api;

// ARCH-08-FR-006: bounded server observations, independent of business audit.
public sealed class HttpServerTelemetry(IMeterFactory factory)
{
    public const string MeterName = "StrataAI.HttpServer";
    private Counter<long>? requests;
    private Histogram<double>? duration;

    public void Record(string method, int status, string outcome, double seconds)
    {
        // Neither instrument publication nor an in-process listener may replace
        // an authoritative response or exception. No request content is read.
        Meter meter;
        try { meter = factory.Create(MeterName); }
        catch { return; /* Optional instrumentation cannot change product behavior. */ }
        KeyValuePair<string, object?>[] tags = [
            new("method", method is "GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE" or "OPTIONS" or "CONNECT" or "TRACE" ? method : "OTHER"),
            new("status_code", status is >= 100 and <= 599 ? status : 0),
            new("outcome", outcome is "completed" or "cancelled" ? outcome : "faulted")];
        try { (requests ??= meter.CreateCounter<long>("strataai.http.server.requests", "{request}")).Add(1, tags); }
        catch { /* Best-effort publication and measurement. */ }
        try { (duration ??= meter.CreateHistogram<double>("strataai.http.server.duration", "s")).Record(seconds, tags); }
        catch { /* Independent best-effort latency observation. */ }
    }
}

public sealed class HttpServerTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, HttpServerTelemetry telemetry)
    {
        var started = Stopwatch.GetTimestamp();
        var method = context.Request.Method;
        var outcome = "faulted";
        try
        {
            await next(context);
            outcome = "completed";
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            // Zero means no completed response; never report an unhandled fault
            // or aborted stream as a successful default 200.
            telemetry.Record(method, outcome == "completed" ? context.Response.StatusCode : 0,
                outcome, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}
