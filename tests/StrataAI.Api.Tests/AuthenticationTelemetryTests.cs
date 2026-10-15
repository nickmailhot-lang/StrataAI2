using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.Auth;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class AuthenticationTelemetryTests
{
    [Theory]
    [InlineData("/auth/register", "register")]
    [InlineData("/auth/login", "login")]
    [InlineData("/auth/password/forgot", "password_forgot")]
    [InlineData("/auth/password/reset", "password_reset")]
    [InlineData("/auth/verification/resend", "verification_resend")]
    [InlineData("/auth/verify-email", "verify_email")]
    [InlineData("/auth/logout", "logout")]
    [InlineData("/AUTH/LOGIN/", "login")]
    public async Task ARCH_08_Real_routing_counts_csrf_refusal_without_reading_private_payload(string path, string operation)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var capture = new Capture(app.Services.GetRequiredService<IMeterFactory>().Create(AuthenticationTelemetry.MeterName));
        using var response = await client.PostAsync(path + "?token=private-query", new StringContent("private-body", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(403, (int)response.StatusCode);
        await capture.WaitAsync(2); capture.AssertPair(operation, 403, "completed");
    }

    [Fact]
    public async Task ARCH_08_Framework_media_rejection_is_overall_http_not_a_matched_auth_operation()
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var factory = app.Services.GetRequiredService<IMeterFactory>();
        using var auth = new Capture(factory.Create(AuthenticationTelemetry.MeterName));
        using var http = new Capture(factory.Create(HttpServerTelemetry.MeterName));
        using var response = await client.PostAsync("/auth/login?token=private-query", new StringContent("private-body"), TestContext.Current.CancellationToken);
        Assert.Equal(403, (int)response.StatusCode); await http.WaitAsync(2);
        Assert.Empty(auth.Samples);
        Assert.Contains(http.Samples, s => s.Name == "strataai.http.server.requests" && s.Value == 1 && Equals(s.Tags["status_code"], 403));
        http.AssertPrivateFieldsExcluded();
    }

    [Fact]
    public async Task ARCH_08_Real_registration_login_denial_and_success_are_independent_aggregates()
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var capture = new Capture(app.Services.GetRequiredService<IMeterFactory>().Create(AuthenticationTelemetry.MeterName));
        client.DefaultRequestHeaders.Add("X-StrataAI-Request", "1"); var ct = TestContext.Current.CancellationToken;
        var email = $"auth-metric-{Guid.NewGuid():N}@example.test"; const string password = "private-fixture-password";
        using var registered = await client.PostAsJsonAsync("/auth/register", new { email, password, displayName = "Private fixture" }, ct);
        using var denied = await client.PostAsJsonAsync("/auth/login", new { email, password = "private-wrong-password" }, ct);
        using var accepted = await client.PostAsJsonAsync("/auth/login", new { email, password }, ct);
        Assert.Equal(201, (int)registered.StatusCode); Assert.Equal(401, (int)denied.StatusCode); Assert.Equal(200, (int)accepted.StatusCode);
        await capture.WaitAsync(6);
        var counts = capture.Samples.Where(s => s.Name == "strataai.authentication.requests").ToArray();
        Assert.Equal(3, counts.Length); Assert.All(counts, s => Assert.Equal(1, s.Value));
        Assert.Contains(counts, s => Equals(s.Tags["operation"], "register") && Equals(s.Tags["status_code"], 201));
        Assert.Contains(counts, s => Equals(s.Tags["operation"], "login") && Equals(s.Tags["status_code"], 401));
        Assert.Contains(counts, s => Equals(s.Tags["operation"], "login") && Equals(s.Tags["status_code"], 200));
        Assert.Equal(3, capture.Samples.Count(s => s.Name == "strataai.authentication.duration"));
        capture.AssertPrivateFieldsExcluded();
    }

    [Theory]
    [InlineData(200)] [InlineData(201)] [InlineData(202)] [InlineData(204)]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(409)]
    [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task ARCH_08_Endpoint_response_is_observed_once_without_reading_request_content(int status)
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(AuthenticationTelemetry.MeterName));
        var context = Context(); var calls = 0;
        await new AuthenticationTelemetryMiddleware(c => { calls++; c.Response.StatusCode = status; c.Response.Headers["X-Result"] = "retained"; return Task.CompletedTask; })
            .InvokeAsync(context, services.GetRequiredService<AuthenticationTelemetry>());
        Assert.Equal(1, calls); Assert.Equal(status, context.Response.StatusCode); Assert.Equal("retained", context.Response.Headers["X-Result"]);
        Assert.Equal(0, context.Request.Body.Position); capture.AssertPair("login", status, "completed");
    }

    [Theory]
    [InlineData("GET", "/auth/login")]
    [InlineData("DELETE", "/auth/logout")]
    [InlineData("POST", "/auth/login/private-secret")]
    [InlineData("POST", "/auth/{userId}")]
    [InlineData("POST", "/me")]
    [InlineData("private-method", "/auth/login")]
    [InlineData("POST", null)]
    public async Task ARCH_08_Only_matched_post_auth_templates_are_observed(string method, string? template)
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(AuthenticationTelemetry.MeterName));
        var context = Context(template); context.Request.Method = method; var calls = 0;
        await new AuthenticationTelemetryMiddleware(_ => { calls++; return Task.CompletedTask; }).InvokeAsync(context, services.GetRequiredService<AuthenticationTelemetry>());
        Assert.Equal(1, calls); Assert.Empty(capture.Samples);
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("cancelled")]
    [InlineData("unrelated-cancellation")]
    public async Task ARCH_08_Fault_or_cancellation_preserves_original_exception_without_false_success(string outcome)
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(AuthenticationTelemetry.MeterName));
        var context = Context();
        if (outcome == "cancelled") context.RequestAborted = new CancellationToken(true);
        Exception failure = outcome == "fault" ? new InvalidOperationException("private-failure") : new OperationCanceledException("private-failure", context.RequestAborted);
        Assert.Same(failure, await Record.ExceptionAsync(() => new AuthenticationTelemetryMiddleware(_ => Task.FromException(failure))
            .InvokeAsync(context, services.GetRequiredService<AuthenticationTelemetry>())));
        capture.AssertPair("login", 0, outcome == "cancelled" ? "cancelled" : "faulted");
    }

    [Theory]
    [InlineData("strataai.authentication.requests", false)]
    [InlineData("strataai.authentication.duration", false)]
    [InlineData("strataai.authentication.requests", true)]
    [InlineData("strataai.authentication.duration", true)]
    public async Task ARCH_08_Throwing_listener_cannot_change_result_or_prevent_other_measurement(string failed, bool throwNext)
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(AuthenticationTelemetry.MeterName), failed);
        var context = Context(); var original = new InvalidOperationException("private-original"); var calls = 0;
        var observed = await Record.ExceptionAsync(() => new AuthenticationTelemetryMiddleware(c =>
        { calls++; c.Response.StatusCode = 202; return throwNext ? Task.FromException(original) : Task.CompletedTask; })
            .InvokeAsync(context, services.GetRequiredService<AuthenticationTelemetry>()));
        if (throwNext) Assert.Same(original, observed); else Assert.Null(observed);
        Assert.Equal(1, calls); Assert.Equal(202, context.Response.StatusCode); Assert.Equal(2, capture.Samples.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ARCH_08_Instrument_factory_failure_does_not_change_auth_behavior(bool throwNext)
    {
        using var factory = new BrokenFactory(); var telemetry = new AuthenticationTelemetry(factory);
        var context = Context(); var original = new InvalidOperationException("private-original"); var calls = 0;
        var observed = await Record.ExceptionAsync(() => new AuthenticationTelemetryMiddleware(c =>
        { calls++; c.Response.StatusCode = 202; return throwNext ? Task.FromException(original) : Task.CompletedTask; }).InvokeAsync(context, telemetry));
        if (throwNext) Assert.Same(original, observed); else Assert.Null(observed);
        Assert.Equal(1, calls); Assert.Equal(202, context.Response.StatusCode); Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public void ARCH_08_Recorder_refuses_unknown_operations_and_bounds_status_and_outcome()
    {
        using var services = Services(); using var capture = new Capture(services.GetRequiredService<IMeterFactory>().Create(AuthenticationTelemetry.MeterName));
        var telemetry = services.GetRequiredService<AuthenticationTelemetry>();
        telemetry.Record("private-operation", 200, "completed", .01); Assert.Empty(capture.Samples);
        telemetry.Record("login", 999, "private-outcome", .01); capture.AssertPair("login", 0, "faulted");
    }

    private static ServiceProvider Services() => new ServiceCollection().AddMetrics().AddSingleton<AuthenticationTelemetry>().BuildServiceProvider();
    private static DefaultHttpContext Context(string? template = "/auth/login")
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST";
        context.Request.Path = "/private-path"; context.Request.QueryString = new("?token=private-token");
        context.Request.Headers.Authorization = "Bearer private-credential"; context.TraceIdentifier = "private-correlation";
        context.Request.Body = new MemoryStream("private-body"u8.ToArray());
        if (template is not null) context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, "private-display-name"));
        return context;
    }
    private sealed class BrokenFactory : IMeterFactory
    {
        public int Calls;
        public Meter Create(MeterOptions options) { Calls++; throw new InvalidOperationException("private-instrument"); }
        public void Dispose() { }
    }
    private sealed class Capture : IDisposable
    {
        private readonly MeterListener listener = new();
        public ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> Samples { get; } = new();
        public Capture(Meter meter, string? failed = null)
        {
            listener.InstrumentPublished = (instrument, observer) => { if (ReferenceEquals(instrument.Meter, meter)) observer.EnableMeasurementEvents(instrument); };
            listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
            listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
            void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                Samples.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
                if (instrument.Name == failed) throw new InvalidOperationException("private-listener");
            }
            listener.Start();
        }
        public async Task WaitAsync(int count)
        {
            for (var attempt = 0; attempt < 300 && Samples.Count < count; attempt++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(count, Samples.Count);
        }
        public void AssertPair(string operation, int status, string outcome)
        {
            Assert.Equal(2, Samples.Count);
            Assert.Contains(Samples, s => s.Name == "strataai.authentication.requests" && s.Value == 1);
            Assert.Contains(Samples, s => s.Name == "strataai.authentication.duration" && double.IsFinite(s.Value) && s.Value >= 0);
            Assert.All(Samples, s => { Assert.Equal(operation, s.Tags["operation"]); Assert.Equal(status, s.Tags["status_code"]); Assert.Equal(outcome, s.Tags["outcome"]); });
            AssertPrivateFieldsExcluded();
        }
        public void AssertPrivateFieldsExcluded() => Assert.All(Samples, s =>
        { Assert.Equal(3, s.Tags.Count); Assert.DoesNotContain(s.Tags.Values.OfType<string>(), v => v.Contains("private", StringComparison.OrdinalIgnoreCase)); });
        public void Dispose() => listener.Dispose();
    }
}
