using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using StrataAI.Infrastructure.Runtime;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class OperatorMetricsTests
{
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
        factory.Create("Unselected.Private").CreateCounter<long>("private-card-content").Add(1,
            new KeyValuePair<string, object?>("private-tenant", "private-retry-key"));
        Assert.True(sdk.ForceFlush(5000));
        var sample = Assert.Single(transport.Batches);
        Assert.Equal("http://collector/v1/metrics", sample.Endpoint);
        Assert.Equal("application/x-protobuf", sample.ContentType);
        Assert.Contains("strataai.checklist.client.events", sample.WireText);
        Assert.Contains("strataai.checklist.client.duration", sample.WireText);
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
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("Disposable collector outage");
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Batches.Enqueue((request.RequestUri!.AbsoluteUri, request.Content.Headers.ContentType?.MediaType, Encoding.UTF8.GetString(bytes)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
    }
}
