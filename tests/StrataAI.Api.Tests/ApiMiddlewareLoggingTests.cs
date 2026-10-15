using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrataAI.Api.Auth;
using StrataAI.Application.Runtime;
using StrataAI.Application.Common;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class ApiMiddlewareLoggingTests
{
    [Theory]
    [InlineData("begin", false)]
    [InlineData("dispose", false)]
    [InlineData("begin", true)]
    [InlineData("dispose", true)]
    public async Task ARCH_08_CorrelationScopeFailurePreservesDownstreamOutcome(string fault, bool nextThrows)
    {
        using var services = Services(); var context = Context(services); var calls = 0;
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "caller-reference_01";
        var failure = new InvalidOperationException("private-request-failure");
        var middleware = new CorrelationIdMiddleware(c =>
        {
            calls++; c.Response.StatusCode = 202; c.Response.Headers["X-Result"] = "saved";
            return nextThrows ? Task.FromException(failure) : Task.CompletedTask;
        });
        var logger = new FaultLogger<CorrelationIdMiddleware>(fault);
        if (nextThrows)
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, logger)));
        else await middleware.InvokeAsync(context, logger);
        Assert.Equal(1, calls); Assert.Equal(202, context.Response.StatusCode);
        Assert.Equal("saved", context.Response.Headers["X-Result"]);
        Assert.Equal("caller-reference_01", context.TraceIdentifier);
        Assert.Equal(context.TraceIdentifier, context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
        Assert.Equal(context.TraceIdentifier, logger.ScopeState!["CorrelationId"]);
        Assert.Equal(1, logger.Begins); Assert.Equal(fault == "begin" ? 0 : 1, logger.Disposals);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task ARCH_08_ThrowingErrorSinkCannotReplacePrivateUnexpectedErrorResponse(string method)
    {
        using var services = Services(); var context = Context(services); context.Request.Method = method;
        await context.Response.Body.WriteAsync("private-unstarted-body"u8.ToArray(), TestContext.Current.CancellationToken);
        context.Response.Headers["X-Private"] = "private-provider-detail";
        var logger = new FaultLogger<UnexpectedApiErrorMiddleware>("log"); var calls = 0;
        var middleware = new UnexpectedApiErrorMiddleware(_ => { calls++; throw new InvalidOperationException("private-request-failure"); });
        await middleware.InvokeAsync(context, logger);
        Assert.Equal(1, calls); Assert.Equal(500, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("X-Private"));
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(context.TraceIdentifier, context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
        if (method == "HEAD") Assert.Empty(Body(context)); else AssertProblem(context, "internal_error");
        var entry = Assert.Single(logger.Entries); Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(new EventId(5000, "UnexpectedApiError"), entry.Id); AssertSafe(entry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_08_StartedOrCancelledFailureStillEscapesWithoutLogging(bool started)
    {
        using var services = Services(); var context = Context(services);
        if (started) context.Features.Set<IHttpResponseFeature>(new StartedResponse());
        else context.RequestAborted = new CancellationToken(true);
        context.Response.Body = new MemoryStream(); context.Response.Headers["X-Result"] = "unchanged";
        await context.Response.Body.WriteAsync("existing-response"u8.ToArray(), TestContext.Current.CancellationToken);
        var failure = new InvalidOperationException("private-request-failure");
        var logger = new FaultLogger<UnexpectedApiErrorMiddleware>("log");
        var middleware = new UnexpectedApiErrorMiddleware(_ => Task.FromException(failure));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, logger)));
        Assert.Empty(logger.Entries); Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("existing-response", Body(context)); Assert.Equal("unchanged", context.Response.Headers["X-Result"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_08_ThrowingWarningCannotReplaceDatabaseSecurityRefusal(bool schema)
    {
        using var services = Services(); var context = Context(services); var calls = 0;
        Exception failure = schema ? new RuntimeDatabaseSchemaException() : new RuntimeDatabaseRoleException();
        var middleware = new RuntimeDatabaseSecurityMiddleware(_ => { calls++; return Task.FromException(failure); });
        var logger = new FaultLogger<RuntimeDatabaseSecurityMiddleware>("log");
        await middleware.InvokeAsync(context, logger);
        Assert.Equal(1, calls); Assert.Equal(503, context.Response.StatusCode);
        AssertProblem(context, schema ? "runtime_database_schema_incompatible" : "runtime_database_role_unsafe");
        var entry = Assert.Single(logger.Entries); Assert.Equal(LogLevel.Warning, entry.Level); AssertSafe(entry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_08_ThrowingWarningCannotReplaceCsrfRefusalOrExecuteMutation(bool crossSite)
    {
        using var services = Services(); var context = Context(services); context.Request.Method = "POST";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "protected mutation"));
        if (crossSite) { context.Request.Headers[CsrfProtectionMiddleware.HeaderName] = "1"; context.Request.Headers["Sec-Fetch-Site"] = "cross-site"; }
        var calls = 0; var middleware = new CsrfProtectionMiddleware(_ => { calls++; return Task.CompletedTask; });
        var logger = new FaultLogger<CsrfProtectionMiddleware>("log"); await middleware.InvokeAsync(context, logger);
        Assert.Equal(0, calls); Assert.Equal(403, context.Response.StatusCode); AssertProblem(context, "csrf_rejected");
        var entry = Assert.Single(logger.Entries); Assert.Equal(LogLevel.Warning, entry.Level); AssertSafe(entry);
    }

    [Fact]
    public async Task ARCH_08_AdmittedMutationStillRunsExactlyOnceWithoutDiagnosticCall()
    {
        using var services = Services(); var context = Context(services); context.Request.Method = "POST";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "protected mutation"));
        context.Request.Headers[CsrfProtectionMiddleware.HeaderName] = "1";
        var calls = 0; var middleware = new CsrfProtectionMiddleware(c => { calls++; c.Response.StatusCode = 204; return Task.CompletedTask; });
        var logger = new FaultLogger<CsrfProtectionMiddleware>("log"); await middleware.InvokeAsync(context, logger);
        Assert.Equal(1, calls); Assert.Equal(204, context.Response.StatusCode); Assert.Empty(logger.Entries); Assert.Empty(Body(context));
    }

    [Fact]
    public async Task ARCH_08_HealthySinkRetainsStructuredCorrelationAndEventIdentity()
    {
        using var services = Services(); var context = Context(services); var logger = new FaultLogger<UnexpectedApiErrorMiddleware>("none");
        var middleware = new UnexpectedApiErrorMiddleware(_ => throw new InvalidOperationException("private-request-failure"));
        await middleware.InvokeAsync(context, logger); Assert.Equal(500, context.Response.StatusCode);
        var entry = Assert.Single(logger.Entries); AssertSafe(entry);
        Assert.Equal("An API request failed. CorrelationId: {CorrelationId}", entry.State["{OriginalFormat}"]);
        Assert.Equal(context.TraceIdentifier, entry.State["CorrelationId"]);
        Assert.Equal(new EventId(5000, "UnexpectedApiError"), entry.Id);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging().BuildServiceProvider();
    private static DefaultHttpContext Context(IServiceProvider services) => new() { RequestServices = services, TraceIdentifier = "safe-reference", Response = { Body = new MemoryStream() } };
    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    private static void AssertProblem(HttpContext context, string code)
    {
        var body = Body(context); using var value = JsonDocument.Parse(body);
        Assert.Equal(code, value.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private-", body); Assert.Contains("application/problem+json", context.Response.ContentType!);
    }
    private static void AssertSafe(Entry entry)
    {
        Assert.Null(entry.Exception); Assert.Contains("safe-reference", entry.Message); Assert.DoesNotContain("private-", entry.Message);
    }
    private sealed class StartedResponse : HttpResponseFeature { public override bool HasStarted => true; }
    private sealed record Entry(LogLevel Level, EventId Id, Exception? Exception, string Message, Dictionary<string, object?> State);
    private sealed class FaultLogger<T>(string fault) : ILogger<T>
    {
        public int Begins, Disposals; public Dictionary<string, object?>? ScopeState; public List<Entry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Begins++; ScopeState = Assert.IsType<Dictionary<string, object?>>(state);
            if (fault == "begin") throw new InvalidOperationException("private-logger-failure");
            return new Scope(this);
        }
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new(level, id, exception, formatter(state, exception), ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
            if (fault == "log") throw new InvalidOperationException("private-logger-failure");
        }
        private void DisposeScope() { Disposals++; if (fault == "dispose") throw new InvalidOperationException("private-logger-failure"); }
        private sealed class Scope(FaultLogger<T> owner) : IDisposable
        {
            public void Dispose() => owner.DisposeScope();
        }
    }
}

public sealed class ApiMiddlewareLoggingHttpTests
{
    [Theory]
    [InlineData("application/json", "unexpected", 500, "internal_error")]
    [InlineData("text/html", "unexpected", 500, "internal_error")]
    [InlineData("application/json", "role", 503, "runtime_database_role_unsafe")]
    [InlineData("application/json", "schema", 503, "runtime_database_schema_incompatible")]
    public async Task ARCH_08_HttpErrorAndLaterRecoverySurviveThrowingSink(string accept, string failure, int status, string code)
    {
        var clock = new ProbeClock(failure); var logs = new ThrowingProvider(); var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => { services.AddSingleton<IClock>(clock); services.AddSingleton<ILoggerProvider>(logs); });
        using var client = app.CreateClient(); using var initial = await client.GetAsync("/api/health", ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode); clock.Fail = true;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health?probe=private-request-failure");
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-Correlation-ID", "http-diagnostics-reference");
        using var response = await client.SendAsync(request, ct); Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("http-diagnostics-reference", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        var body = await response.Content.ReadAsStringAsync(ct); Assert.DoesNotContain("private-", body);
        using var document = JsonDocument.Parse(body); Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.Equal("http-diagnostics-reference", document.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(1, logs.Calls); clock.Fail = false;
        using var recovered = await client.GetAsync("/api/health", ct); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(1, logs.Calls);
    }

    [Fact]
    public async Task ARCH_08_HttpCsrfRefusalKeepsItsCodeWhenDiagnosticProviderThrows()
    {
        var logs = new ThrowingProvider(); var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ILoggerProvider>(logs));
        using var client = app.CreateClient(); using var request = new HttpRequestMessage(HttpMethod.Post, "/organizations");
        request.Headers.Add("X-Correlation-ID", "http-diagnostics-reference"); request.Content = JsonContent.Create(new { name = "private-organization" });
        using var refused = await client.SendAsync(request, ct); Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("csrf_rejected", body.GetProperty("code").GetString());
        Assert.Equal("http-diagnostics-reference", body.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("private-", body.GetRawText()); Assert.Equal(1, logs.Calls);
        using var healthy = await client.GetAsync("/api/health", ct); Assert.Equal(HttpStatusCode.OK, healthy.StatusCode); Assert.Equal(1, logs.Calls);
    }

    private sealed class ProbeClock(string failure) : IClock
    {
        public bool Fail;
        public DateTimeOffset UtcNow => !Fail ? DateTimeOffset.UtcNow : throw (failure switch
        {
            "role" => (Exception)new RuntimeDatabaseRoleException(),
            "schema" => new RuntimeDatabaseSchemaException(),
            _ => new InvalidOperationException("private-provider-failure"),
        });
    }
    private sealed class ThrowingProvider : ILoggerProvider
    {
        public int Calls;
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }
        private sealed class Logger(ThrowingProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => category == typeof(UnexpectedApiErrorMiddleware).FullName
                || category == typeof(RuntimeDatabaseSecurityMiddleware).FullName || category == typeof(CsrfProtectionMiddleware).FullName;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(level)) return;
                Interlocked.Increment(ref owner.Calls);
                throw new InvalidOperationException("private-logger-failure");
            }
        }
    }
}
