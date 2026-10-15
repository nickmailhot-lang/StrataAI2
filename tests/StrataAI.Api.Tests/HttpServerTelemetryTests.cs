using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class HttpServerTelemetryTests
{
    [Fact]
    public async Task ARCH_08_Real_pipeline_includes_health_missing_routes_authentication_and_csrf_refusals()
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var capture = new Capture(app.Services.GetRequiredService<IMeterFactory>().Create(HttpServerTelemetry.MeterName));
        var ct = TestContext.Current.CancellationToken;
        using var health = await client.GetAsync("/healthz", ct);
        using var missing = await client.GetAsync("/private-missing?token=private-secret", ct);
        using var anonymous = await client.GetAsync("/me", ct);
        using var csrf = await client.PostAsync("/auth/login", new StringContent("private-content"), ct);
        Assert.Equal(200, (int)health.StatusCode); Assert.Equal(404, (int)missing.StatusCode);
        Assert.Equal(401, (int)anonymous.StatusCode); Assert.Equal(403, (int)csrf.StatusCode);
        for (var attempt = 0; attempt < 100 && capture.Samples.Count < 8; attempt++) await Task.Delay(10, ct);
        var requests = capture.Samples.Where(s => s.Name == "strataai.http.server.requests").ToArray();
        Assert.Equal(4, requests.Length); Assert.All(requests, s => Assert.Equal(1, s.Value));
        Assert.Equal(new[] { 200, 401, 403, 404 }, requests.Select(s => (int)s.Tags["status_code"]!).Order().ToArray());
        Assert.All(capture.Samples, s =>
        {
            Assert.Equal(3, s.Tags.Count); Assert.Equal("completed", s.Tags["outcome"]);
            Assert.DoesNotContain(s.Tags.Values.OfType<string>(), v => v.Contains("private", StringComparison.Ordinal));
        });
        Assert.Equal(4, capture.Samples.Count(s => s.Name == "strataai.http.server.duration"));
    }

    [Theory]
    [InlineData(200)] [InlineData(204)] [InlineData(400)] [InlineData(401)]
    [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(429)]
    [InlineData(500)] [InlineData(503)]
    public async Task ARCH_08_Http_outcomes_measure_once_without_private_request_fields(int status)
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(HttpServerTelemetry.MeterName));
        var context = Context(); var calls = 0;
        await new HttpServerTelemetryMiddleware(c =>
        {
            calls++; c.Response.StatusCode = status; c.Response.Headers["X-Result"] = "retained";
            return Task.CompletedTask;
        }).InvokeAsync(context, services.GetRequiredService<HttpServerTelemetry>());
        Assert.Equal(1, calls); Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("retained", context.Response.Headers["X-Result"]);
        AssertSamples(capture, status, "completed", "POST");
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ARCH_08_Fault_and_cancellation_preserve_original_exception_without_false_200(bool cancelled)
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(HttpServerTelemetry.MeterName)); var context = Context();
        if (cancelled) context.RequestAborted = new CancellationToken(true);
        Exception error = cancelled ? new OperationCanceledException(context.RequestAborted) : new InvalidOperationException("private-failure");
        var middleware = new HttpServerTelemetryMiddleware(_ => Task.FromException(error));
        Assert.Same(error, await Record.ExceptionAsync(() => middleware.InvokeAsync(context, services.GetRequiredService<HttpServerTelemetry>())));
        AssertSamples(capture, 0, cancelled ? "cancelled" : "faulted", "POST");
    }

    [Theory]
    [InlineData("strataai.http.server.requests", false)]
    [InlineData("strataai.http.server.duration", false)]
    [InlineData("strataai.http.server.requests", true)]
    [InlineData("strataai.http.server.duration", true)]
    public async Task ARCH_08_Throwing_listener_cannot_replace_response_or_other_measurement(string failed, bool nextThrows)
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(HttpServerTelemetry.MeterName), failed); var context = Context();
        var failure = new InvalidOperationException("private-original-failure");
        var middleware = new HttpServerTelemetryMiddleware(c => { c.Response.StatusCode = 202; return nextThrows ? Task.FromException(failure) : Task.CompletedTask; });
        var observed = await Record.ExceptionAsync(() => middleware.InvokeAsync(context, services.GetRequiredService<HttpServerTelemetry>()));
        if (nextThrows) Assert.Same(failure, observed); else Assert.Null(observed);
        Assert.Equal(202, context.Response.StatusCode); Assert.Equal(2, capture.Samples.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ARCH_08_Instrument_creation_failure_cannot_replace_response_or_exception(bool nextThrows)
    {
        using var factory = new BrokenMeterFactory(); var telemetry = new HttpServerTelemetry(factory);
        var context = Context(); var failure = new InvalidOperationException("private-original-failure"); var calls = 0;
        var middleware = new HttpServerTelemetryMiddleware(c =>
        {
            calls++; c.Response.StatusCode = 202;
            return nextThrows ? Task.FromException(failure) : Task.CompletedTask;
        });
        var observed = await Record.ExceptionAsync(() => middleware.InvokeAsync(context, telemetry));
        if (nextThrows) Assert.Same(failure, observed); else Assert.Null(observed);
        Assert.Equal(1, calls); Assert.Equal(202, context.Response.StatusCode); Assert.Equal(1, factory.Calls);
    }

    private sealed class BrokenMeterFactory : IMeterFactory
    {
        public int Calls { get; private set; }
        public Meter Create(MeterOptions options) { Calls++; throw new InvalidOperationException("private-instrument-failure"); }
        public void Dispose() { }
    }

    [Fact]
    public async Task ARCH_08_Unknown_method_is_bounded_and_unrelated_cancellation_is_a_fault()
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(HttpServerTelemetry.MeterName)); var context = Context();
        context.Request.Method = "private-custom-method";
        var error = new OperationCanceledException("private-provider-timeout");
        Assert.Same(error, await Record.ExceptionAsync(() => new HttpServerTelemetryMiddleware(_ => Task.FromException(error))
            .InvokeAsync(context, services.GetRequiredService<HttpServerTelemetry>())));
        AssertSamples(capture, 0, "faulted", "OTHER");
    }

    private static ServiceProvider Services() => new ServiceCollection().AddMetrics().AddSingleton<HttpServerTelemetry>().BuildServiceProvider();
    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST";
        context.Request.Path = "/private-tenant/private-card"; context.Request.QueryString = new("?token=private-secret");
        context.Request.Headers.Authorization = "Bearer private-credential";
        context.Request.Body = new MemoryStream("private-content"u8.ToArray());
        context.TraceIdentifier = "private-correlation"; return context;
    }
    private static void AssertSamples(Capture capture, int status, string outcome, string method)
    {
        var samples = capture.Samples.ToArray(); Assert.Equal(2, samples.Length);
        Assert.Contains(samples, s => s.Name == "strataai.http.server.requests" && s.Value == 1);
        Assert.Contains(samples, s => s.Name == "strataai.http.server.duration" && double.IsFinite(s.Value) && s.Value >= 0);
        Assert.All(samples, s =>
        {
            Assert.Equal(3, s.Tags.Count); Assert.Equal(status, s.Tags["status_code"]);
            Assert.Equal(outcome, s.Tags["outcome"]); Assert.Equal(method, s.Tags["method"]);
            Assert.DoesNotContain(s.Tags.Values.OfType<string>(), value => value.Contains("private", StringComparison.Ordinal));
        });
    }
    private sealed class Capture : IDisposable
    {
        private readonly MeterListener listener = new();
        public ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> Samples { get; } = new();
        public Capture(Meter meter, string? failed = null)
        {
            listener.InstrumentPublished = (instrument, observer) =>
            {
                if (ReferenceEquals(instrument.Meter, meter)) observer.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((i, value, tags, _) => Add(i, value, tags));
            listener.SetMeasurementEventCallback<double>((i, value, tags, _) => Add(i, value, tags));
            void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                Samples.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
                if (instrument.Name == failed) throw new InvalidOperationException("private-listener-failure");
            }
            listener.Start();
        }
        public void Dispose() => listener.Dispose();
    }
}
