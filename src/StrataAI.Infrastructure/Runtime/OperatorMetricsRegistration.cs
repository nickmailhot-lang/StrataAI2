using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace StrataAI.Infrastructure.Runtime;

public static class OperatorMetricsRegistration
{
    public const string ExporterName = "strataai-operator-metrics";
    public static bool AddStrataAiOperatorMetrics(this IServiceCollection services, IConfiguration configuration, Assembly hostAssembly) =>
        AddMetrics(services, configuration, hostAssembly, worker: false);

    public static bool AddStrataAiWorkerMetrics(this IServiceCollection services, IConfiguration configuration, Assembly hostAssembly) =>
        AddMetrics(services, configuration, hostAssembly, worker: true);

    private static bool AddMetrics(IServiceCollection services, IConfiguration configuration, Assembly hostAssembly, bool worker)
    {
        var value = configuration["STRATAAI_METRICS_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || string.IsNullOrEmpty(endpoint.Host)
            || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment)
            || !endpoint.AbsolutePath.EndsWith("/v1/metrics", StringComparison.Ordinal))
            throw new InvalidOperationException("STRATAAI_METRICS_OTLP_ENDPOINT requires an HTTP(S) metrics endpoint without user info, query or fragment.");
        var build = BuildIdentityReader.Read(hostAssembly);
        // No resource detectors or automatic HTTP/database instrumentation: those
        // may carry machine identity, paths, queries or protected route values.
        var resource = ResourceBuilder.CreateEmpty().AddAttributes(new Dictionary<string, object>
        {
            ["service.name"] = worker ? "strataai-worker" : "strataai-api", ["service.namespace"] = "strataai",
            ["service.version"] = build.Version, ["strataai.build.revision"] = build.Revision,
        });
        services.Configure<OtlpExporterOptions>(ExporterName, options =>
            {
                options.Endpoint = endpoint; options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Headers = configuration["STRATAAI_METRICS_OTLP_HEADERS"] ?? string.Empty;
                options.TimeoutMilliseconds = 3000;
                options.HttpClientFactory = () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                    { Timeout = TimeSpan.FromSeconds(3) };
            });
        services.Configure<MetricReaderOptions>(ExporterName, reader =>
            {
                reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds = 3000;
            });
        var meters = worker ? new[] { "StrataAI.WorkerJobs" }
            : new[] { "StrataAI.BoardSharing", "StrataAI.ChecklistClient", "StrataAI.ActivityClient", "StrataAI.Organizations", "StrataAI.HttpServer", "StrataAI.Authentication" };
        services.AddOpenTelemetry().WithMetrics(metrics =>
        {
            metrics.SetResourceBuilder(resource).AddMeter(meters);
            if (!worker)
            {
                // These custom instruments measure seconds. SDK generic
                // defaults do not resolve the 400/750 ms service objectives.
                metrics.AddView("strataai.http.server.duration", ServerDurationBuckets());
                metrics.AddView("strataai.authentication.duration", ServerDurationBuckets());
            }
            metrics.AddOtlpExporter(ExporterName, configure: (Action<OtlpExporterOptions>?)null);
        });
        return true;
    }

    private static ExplicitBucketHistogramConfiguration ServerDurationBuckets() => new()
    {
        Boundaries = [0, .005, .01, .025, .05, .075, .1, .25, .4, .5, .75, 1, 2.5, 5, 7.5, 10, 30],
    };
}
