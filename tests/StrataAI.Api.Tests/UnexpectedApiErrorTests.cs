using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrataAI.Application.Common;
using StrataAI.Application.Runtime;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    public async Task Unexpected_errors_are_private_correlated_and_leave_the_host_usable(string accept)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ErrorProbeClock();
        var logs = new ErrorProbeLogs();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<ILoggerProvider>(logs);
        });
        using var client = app.CreateClient();
        using var healthy = await client.GetAsync("/api/health", ct);
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        clock.Fail = true;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health?probe=private-request-probe");
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-Correlation-ID", "unexpected-error-reference");
        using var refused = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.True(refused.Headers.CacheControl?.NoStore);
        var error = await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("internal_error", error.GetProperty("code").GetString());
        Assert.Equal("The request could not be completed. Try again.", error.GetProperty("message").GetString());
        Assert.Equal("unexpected-error-reference", error.GetProperty("correlationId").GetString());
        Assert.Equal(error.GetProperty("correlationId").GetString(), refused.Headers.GetValues("X-Correlation-ID").Single());
        Assert.DoesNotContain("private-", error.GetRawText());
        var log = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Equal(5000, log.Event.Id);
        Assert.Null(log.Exception);
        Assert.Contains("unexpected-error-reference", log.Message);
        Assert.DoesNotContain("private-", log.Message);
        Assert.DoesNotContain("/api/health", log.Message);
        clock.Fail = false;
        using var recovered = await client.GetAsync("/api/health", ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task Unavailable_readiness_keeps_operator_status_and_the_error_contract()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services =>
            services.AddSingleton<IRuntimeDependencyStatus>(new ErrorProbeNotReady()));
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/readyz");
        request.Headers.Add("X-Correlation-ID", "readiness-error-reference");
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("not-ready", body.GetProperty("status").GetString());
        Assert.Equal("runtime_not_ready", body.GetProperty("code").GetString());
        Assert.Equal("The service is not ready. Try again later.", body.GetProperty("message").GetString());
        Assert.Equal("readiness-error-reference", body.GetProperty("correlationId").GetString());
        Assert.Equal(body.GetProperty("correlationId").GetString(), response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.False(body.TryGetProperty("detail", out _));
        Assert.False(body.TryGetProperty("mode", out _));
    }

    [Theory]
    [InlineData(false, "runtime_database_role_unsafe")]
    [InlineData(true, "runtime_database_schema_incompatible")]
    public async Task Specialized_database_refusals_keep_their_service_unavailable_codes(bool schema, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ErrorProbeClock
        {
            Failure = schema ? new RuntimeDatabaseSchemaException() : new RuntimeDatabaseRoleException(),
        };
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var client = app.CreateClient();
        using var healthy = await client.GetAsync("/healthz", ct);
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        clock.Fail = true;
        using var response = await client.GetAsync("/api/health", ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal("The service is temporarily unavailable.", body.GetProperty("message").GetString());
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), body.GetProperty("correlationId").GetString());
    }

    private sealed class ErrorProbeClock : IClock
    {
        public bool Fail { get; set; }
        public Exception Failure { get; init; } = new InvalidOperationException("private-provider-probe");
        public DateTimeOffset UtcNow => Fail ? throw Failure : DateTimeOffset.UtcNow;
    }

    private sealed class ErrorProbeNotReady : IRuntimeDependencyStatus
    {
        public RuntimeMode Mode => RuntimeMode.Demo;
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class ErrorProbeLogs : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, EventId Event, Exception? Exception, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new ProbeLogger(this, categoryName);
        public void Dispose() { }
        private sealed class ProbeLogger(ErrorProbeLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => category == typeof(UnexpectedApiErrorMiddleware).FullName;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) owner.Entries.Enqueue((logLevel, eventId, exception, formatter(state, exception)));
            }
        }
    }
}

public sealed class UnexpectedApiErrorBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Started_or_cancelled_requests_preserve_the_original_failure_and_response(bool started)
    {
        var context = new DefaultHttpContext();
        if (started) context.Features.Set<IHttpResponseFeature>(new StartedErrorResponse());
        else context.RequestAborted = new CancellationToken(true);
        context.Response.Body = new MemoryStream();
        await context.Response.Body.WriteAsync("existing-response"u8.ToArray(), TestContext.Current.CancellationToken);
        context.Response.Headers["X-Existing"] = "unchanged";
        var failure = new InvalidOperationException("private-boundary-probe");
        var middleware = new UnexpectedApiErrorMiddleware(_ => Task.FromException(failure));
        var escaped = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UnexpectedApiErrorMiddleware>.Instance));
        Assert.Same(failure, escaped);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("unchanged", context.Response.Headers["X-Existing"]);
        Assert.Equal("existing-response"u8.ToArray(), ((MemoryStream)context.Response.Body).ToArray());
    }

    [Fact]
    public async Task Unexpected_head_errors_clear_unstarted_response_and_keep_bodyless_semantics()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "HEAD";
        context.TraceIdentifier = "head-error-reference";
        context.Response.Body = new MemoryStream();
        await context.Response.Body.WriteAsync("unstarted-private-response"u8.ToArray(), TestContext.Current.CancellationToken);
        context.Response.ContentLength = 26;
        context.Response.Headers["X-Private"] = "private-probe";
        var middleware = new UnexpectedApiErrorMiddleware(_ => Task.FromException(new InvalidOperationException("private-probe")));
        await middleware.InvokeAsync(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnexpectedApiErrorMiddleware>.Instance);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Empty(((MemoryStream)context.Response.Body).ToArray());
        Assert.Null(context.Response.ContentLength);
        Assert.False(context.Response.Headers.ContainsKey("X-Private"));
        Assert.Equal("head-error-reference", context.Response.Headers["X-Correlation-ID"]);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    private sealed class StartedErrorResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
