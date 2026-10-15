using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Infrastructure.Runtime;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class OperatorMetricsTests
{
    [Fact]
    public void Worker_export_is_disabled_without_explicit_configuration()
    {
        var services = new ServiceCollection();
        Assert.False(services.AddStrataAiWorkerMetrics(Configuration(null), typeof(Program).Assembly));
        Assert.Empty(services);
    }

    [Fact]
    public async Task Worker_export_has_its_own_service_identity_and_excludes_API_meters()
    {
        using var transport = new MetricsTransport();
        var services = new ServiceCollection(); services.AddLogging(); services.AddMetrics();
        Assert.True(services.AddStrataAiWorkerMetrics(Configuration("http://collector/v1/metrics"), typeof(Program).Assembly));
        services.PostConfigure<OtlpExporterOptions>(OperatorMetricsRegistration.ExporterName,
            options => options.HttpClientFactory = () => new HttpClient(transport, disposeHandler: false));
        using var provider = services.BuildServiceProvider(); var sdk = provider.GetRequiredService<MeterProvider>();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var telemetry = new WorkerJobTelemetry(factory);
        using var backlog = new OrganizationBacklogWorker(new OrganizationBacklogTests.Reader((_, _) =>
            Task.FromResult(new OrganizationJobBacklog(2, 1))), new([Guid.NewGuid()]), factory);
        await backlog.SampleAsync(TestContext.Current.CancellationToken);
        telemetry.Record(JobProcessingResult.Completed);
        telemetry.RecordOrganizationLoopFailure(); telemetry.RecordIdentityEmailLoopFailure();
        factory.Create("StrataAI.HttpServer").CreateCounter<long>("private-api-observation").Add(1);
        Assert.True(sdk.ForceFlush(5000)); var sample = Assert.Single(transport.Batches);
        Assert.Contains("strataai.worker.jobs", sample.WireText); Assert.Contains("strataai-worker", sample.WireText);
        Assert.Contains("strataai.worker.loop.failures", sample.WireText); Assert.Contains("identity_email", sample.WireText);
        Assert.Contains("strataai.worker.scoped.backlog", sample.WireText);
        Assert.Contains("strataai.worker.scoped.backlog.available", sample.WireText);
        Assert.Contains("pending", sample.WireText); Assert.Contains("running", sample.WireText);
        Assert.Contains("strataai.build.revision", sample.WireText);
        Assert.DoesNotContain("strataai-api", sample.WireText); Assert.DoesNotContain("private", sample.WireText);
        transport.Fail = true;
        telemetry.Record(JobProcessingResult.Completed); telemetry.RecordOrganizationLoopFailure();
        Assert.Null(Record.Exception(() => sdk.ForceFlush(5000)));
    }

    [Fact]
    public void Metrics_export_is_disabled_without_explicit_configuration()
    {
        var services = new ServiceCollection();
        Assert.False(services.AddStrataAiOperatorMetrics(Configuration(null), typeof(Program).Assembly));
        Assert.Empty(services);
    }

    [Theory]
    [InlineData("file:///private/v1/metrics")]
    [InlineData("http://user:private-secret@collector/v1/metrics")]
    [InlineData("http://collector/v1/metrics?token=private-secret")]
    [InlineData("http://collector/v1/metrics#private-secret")]
    [InlineData("http://collector/v1/traces")]
    [InlineData("relative/v1/metrics")]
    public void Invalid_operator_configuration_fails_without_echoing_input(string endpoint)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddStrataAiOperatorMetrics(Configuration(endpoint), typeof(Program).Assembly));
        Assert.DoesNotContain("private-secret", error.Message);
    }

    [Fact]
    public void Configured_export_sends_real_otlp_metrics_and_excludes_unselected_meters()
    {
        using var transport = new MetricsTransport();
        var services = new ServiceCollection(); services.AddLogging(); services.AddMetrics();
        Assert.True(services.AddStrataAiOperatorMetrics(Configuration("http://collector/v1/metrics"), typeof(Program).Assembly));
        services.PostConfigure<OtlpExporterOptions>(OperatorMetricsRegistration.ExporterName,
            options => options.HttpClientFactory = () => new HttpClient(transport, disposeHandler: false));
        using var provider = services.BuildServiceProvider();
        var sdk = provider.GetRequiredService<MeterProvider>();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var checklist = factory.Create("StrataAI.ChecklistClient");
        checklist.CreateCounter<long>("strataai.checklist.client.events").Add(2,
            new KeyValuePair<string, object?>("action", "disclosure"), new KeyValuePair<string, object?>("kind", "open"));
        checklist.CreateHistogram<double>("strataai.checklist.client.duration", "s").Record(.125,
            new KeyValuePair<string, object?>("action", "item_update"), new KeyValuePair<string, object?>("kind", "success"));
        var activity = factory.Create("StrataAI.ActivityClient");
        activity.CreateCounter<long>("strataai.activity.client.events").Add(1,
            new KeyValuePair<string, object?>("action", "card_disclosure"), new KeyValuePair<string, object?>("kind", "open"));
        activity.CreateHistogram<double>("strataai.activity.client.duration", "s").Record(.125,
            new KeyValuePair<string, object?>("action", "card_read"), new KeyValuePair<string, object?>("kind", "success"));
        var organizations = factory.Create("StrataAI.Organizations");
        var http = factory.Create("StrataAI.HttpServer");
        http.CreateCounter<long>("strataai.http.server.requests").Add(1,
            new KeyValuePair<string, object?>("method", "POST"), new KeyValuePair<string, object?>("status_code", 401),
            new KeyValuePair<string, object?>("outcome", "completed"));
        http.CreateHistogram<double>("strataai.http.server.duration", "s").Record(.125,
            new KeyValuePair<string, object?>("method", "POST"), new KeyValuePair<string, object?>("status_code", 401),
            new KeyValuePair<string, object?>("outcome", "completed"));
        organizations.CreateCounter<long>("strataai.organization.requests").Add(1,
            new KeyValuePair<string, object?>("operation", "create"), new KeyValuePair<string, object?>("outcome", "success"));
        organizations.CreateHistogram<double>("strataai.organization.duration", "s").Record(.125,
            new KeyValuePair<string, object?>("operation", "create"), new KeyValuePair<string, object?>("outcome", "success"));
        factory.Create("Unselected.Private").CreateCounter<long>("private-card-content").Add(1,
            new KeyValuePair<string, object?>("private-tenant", "private-retry-key"));
        Assert.True(sdk.ForceFlush(5000));
        var sample = Assert.Single(transport.Batches);
        Assert.Equal("http://collector/v1/metrics", sample.Endpoint);
        Assert.Equal("application/x-protobuf", sample.ContentType);
        Assert.Contains("strataai.checklist.client.events", sample.WireText);
        Assert.Contains("strataai.checklist.client.duration", sample.WireText);
        Assert.Contains("strataai.activity.client.events", sample.WireText);
        Assert.Contains("strataai.activity.client.duration", sample.WireText);
        Assert.Contains("strataai.organization.requests", sample.WireText);
        Assert.Contains("strataai.organization.duration", sample.WireText);
        Assert.Contains("strataai.http.server.requests", sample.WireText);
        Assert.Contains("strataai.http.server.duration", sample.WireText);
        Assert.Contains("disclosure", sample.WireText); Assert.Contains("item_update", sample.WireText);
        Assert.Contains("service.name", sample.WireText); Assert.Contains("strataai-api", sample.WireText);
        Assert.Contains("strataai.build.revision", sample.WireText);
        Assert.DoesNotContain("private", sample.WireText); Assert.DoesNotContain("service.instance.id", sample.WireText);
        transport.Fail = true;
        checklist.CreateCounter<long>("strataai.checklist.client.fail_test").Add(1);
        // Collector outage cannot throw into authoritative product operations.
        var error = Record.Exception(() => { sdk.ForceFlush(5000); }); Assert.Null(error);
    }

    private static IConfiguration Configuration(string? endpoint) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["STRATAAI_METRICS_OTLP_ENDPOINT"] = endpoint }).Build();
    private sealed class MetricsTransport : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public ConcurrentQueue<(string Endpoint, string? ContentType, string WireText)> Batches { get; } = new();
        // OTLP's .NET exporter uses HttpClient.Send on this target framework.
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            SendAsync(request, cancellationToken).GetAwaiter().GetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("Disposable collector outage");
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Batches.Enqueue((request.RequestUri!.AbsoluteUri, request.Content.Headers.ContentType?.MediaType, Encoding.UTF8.GetString(bytes)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
    }
}
