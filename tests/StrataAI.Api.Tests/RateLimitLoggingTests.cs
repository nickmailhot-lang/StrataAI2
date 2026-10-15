using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StrataAI.Api.Auth;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class RateLimitLoggingTests
{
    [Theory]
    [InlineData("none", null, 60)]
    [InlineData("write", null, 60)]
    [InlineData("create", null, 60)]
    [InlineData("none", 2100, 3)]
    [InlineData("write", 2100, 3)]
    [InlineData("create", 2100, 3)]
    [InlineData("write", 0, 1)]
    [InlineData("create", -100, 1)]
    [InlineData("none", 1, 1)]
    public async Task ARCH_08_RateRefusalAndRetryHintSurviveDiagnosticFailure(string failure, int? milliseconds, int seconds)
    {
        using var sink = new Sink(failure); var services = new ServiceCollection(); services.AddLogging();
        services.AddSecurityRateLimits(new ConfigurationBuilder().Build()); services.AddSingleton<ILoggerFactory>(sink);
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider, TraceIdentifier = "safe-rate-reference", Response = { Body = new MemoryStream() } };
        using var lease = new Lease(milliseconds);
        var reject = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected;
        Assert.NotNull(reject); await reject(new OnRejectedContext { HttpContext = context, Lease = lease }, TestContext.Current.CancellationToken);
        Assert.Equal(429, context.Response.StatusCode); Assert.Equal(seconds.ToString(), context.Response.Headers.RetryAfter);
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        using var document = JsonDocument.Parse(body); Assert.Equal("rate_limit_exceeded", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("Too many requests. Please wait and retry.", document.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("private-", body); Assert.Equal(1, sink.Creations);
        if (failure == "create") Assert.Empty(sink.Entries);
        else AssertEntry(Assert.Single(sink.Entries), context.TraceIdentifier);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("write")]
    [InlineData("create")]
    public async Task ARCH_08_RealAuthQuotaStillRejectsAndHostStaysUsableWhenLoggingFails(string failure)
    {
        using var sink = new Sink(failure); await using var app = new Factory(sink); using var client = app.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        // Small sequential functional fixture at the supported minimum configured
        // quota. No concurrency, throughput measurement or production-limit change.
        for (var index = 0; index < 10; index++)
        {
            using var request = Login(); using var response = await client.SendAsync(request, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Equal(0, sink.Creations); Assert.Empty(sink.Entries);
        for (var index = 0; index < 2; index++)
        {
            using var request = Login(); request.Headers.Add(CorrelationIdMiddleware.HeaderName, "http-rate-reference");
            using var response = await client.SendAsync(request, ct); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.NotNull(response.Headers.RetryAfter?.Delta); Assert.InRange(response.Headers.RetryAfter.Delta.Value.TotalSeconds, 1, 60);
            Assert.Equal("http-rate-reference", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            Assert.Equal("rate_limit_exceeded", body.GetProperty("code").GetString());
            Assert.Equal("http-rate-reference", body.GetProperty("correlationId").GetString());
            Assert.Equal("Too many requests. Please wait and retry.", body.GetProperty("message").GetString());
            Assert.DoesNotContain("private-", body.GetRawText());
        }
        using var healthy = await client.GetAsync("/api/health", ct); Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        Assert.Equal(failure == "create" ? 2 : 1, sink.Creations);
        Assert.Equal(failure == "create" ? 0 : 2, sink.Entries.Count);
        foreach (var entry in sink.Entries) AssertEntry(entry, "http-rate-reference");
    }

    private static HttpRequestMessage Login()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login"); request.Headers.Add(CsrfProtectionMiddleware.HeaderName, "1");
        request.Content = JsonContent.Create(new { email = "private-unknown-account@example.test", password = "private-login-payload" }); return request;
    }
    private static void AssertEntry(Entry entry, string correlation)
    {
        Assert.Equal(LogLevel.Warning, entry.Level); Assert.Null(entry.Exception);
        Assert.Equal("RATE_LIMIT_TRIGGERED CorrelationId={CorrelationId}", entry.State["{OriginalFormat}"]);
        Assert.Equal(correlation, entry.State["CorrelationId"]); Assert.Contains(correlation, entry.Message);
        Assert.DoesNotContain("private-", entry.Message);
    }
    private sealed class Factory(Sink sink) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(sink));
        }
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["STRATAAI_RUNTIME_MODE"] = "demo", ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = "false", ["ConnectionStrings:Postgres"] = "",
                ["STRATAAI_AUTH_REQUESTS_PER_MINUTE"] = "10", ["Logging:LogLevel:Default"] = "Warning",
            }));
            return base.CreateHost(builder);
        }
    }
    private sealed class Lease(int? milliseconds) : RateLimitLease
    {
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => milliseconds.HasValue ? [MetadataName.RetryAfter.Name] : [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = metadataName == MetadataName.RetryAfter.Name && milliseconds.HasValue ? TimeSpan.FromMilliseconds(milliseconds.Value) : null;
            return metadata is not null;
        }
    }
    private sealed record Entry(LogLevel Level, Exception? Exception, string Message, Dictionary<string, object?> State);
    private sealed class Sink(string failure) : ILoggerFactory, ILoggerProvider
    {
        public int Creations; public ConcurrentQueue<Entry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName)
        {
            if (categoryName != "SecurityRateLimits") return NullLogger.Instance;
            Interlocked.Increment(ref Creations);
            if (failure == "create") throw new InvalidOperationException("private-logger-creation-failure");
            return new Logger(this, failure);
        }
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        private sealed class Logger(Sink owner, string failure) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Entries.Enqueue(new(level, exception, formatter(state, exception), ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
                if (failure == "write") throw new InvalidOperationException("private-logger-write-failure");
            }
        }
    }
}
