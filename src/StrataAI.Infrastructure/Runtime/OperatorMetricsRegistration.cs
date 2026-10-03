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
    public static bool AddStrataAiOperatorMetrics(this IServiceCollection services, IConfiguration configuration, Assembly hostAssembly)
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
            ["service.name"] = "strataai-api", ["service.namespace"] = "strataai",
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
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.SetResourceBuilder(resource)
            .AddMeter("StrataAI.BoardSharing", "StrataAI.ChecklistClient")
            .AddOtlpExporter(ExporterName, configure: (Action<OtlpExporterOptions>?)null));
        return true;
    }
}
