using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.Organizations;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class BusinessTelemetryFailureTests
{
    public static TheoryData<string, string, string> Cases
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var area in new[] { "organization", "board_sharing" })
            foreach (var fault in new[] { "healthy", "factory", "publish_requests", "publish_duration", "measure_requests", "measure_duration" })
            foreach (var outcome in new[] { "success", "fault", "cancelled" }) rows.Add(area, fault, outcome);
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ARCH_08_Business_instrumentation_cannot_replace_execution_or_other_measurement(string area, string fault, string outcome)
    {
        using var factory = new BusinessMeterFactory(area, fault);
        using var capture = new BusinessMetricCapture(factory.Meter, fault);
        var context = Context(area);
        if (outcome == "cancelled") context.RequestAborted = new CancellationToken(true);
        Exception original = outcome == "cancelled" ? new OperationCanceledException("private-original", context.RequestAborted)
            : new InvalidOperationException("private-original");
        var calls = 0;
        RequestDelegate next = c => { calls++; c.Response.StatusCode = 202; return outcome == "success" ? Task.CompletedTask : Task.FromException(original); };
        var observed = await Record.ExceptionAsync(async () =>
        {
            if (area == "organization") await new OrganizationTelemetryMiddleware(next).InvokeAsync(context, new OrganizationTelemetry(factory));
            else await new BoardSharingTelemetryMiddleware(next).InvokeAsync(context, new BoardSharingTelemetry(factory));
        });
        Assert.Equal(1, calls);
        if (outcome == "success") Assert.Null(observed); else Assert.Same(original, observed);
        Assert.Equal(202, context.Response.StatusCode);
        var expected = fault == "factory" ? 0 : fault.StartsWith("publish_", StringComparison.Ordinal) ? 1 : 2;
        Assert.Equal(expected, capture.Samples.Count);
        if (fault == "publish_requests") Assert.EndsWith(".duration", Assert.Single(capture.Samples).Name);
        if (fault == "publish_duration") Assert.EndsWith(".requests", Assert.Single(capture.Samples).Name);
        Assert.All(capture.Samples, sample =>
        {
            Assert.Equal(new[] { "error_code", "keyed_attempt", "operation", "outcome" }, sample.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(area == "organization" ? "create" : "board_read", sample.Tags["operation"]);
            Assert.Equal(outcome == "success" ? "success" : outcome == "cancelled" ? "cancelled" : "unavailable", sample.Tags["outcome"]);
            Assert.Equal(outcome == "success" ? "none" : "server_error", sample.Tags["error_code"]);
            Assert.Equal(true, sample.Tags["keyed_attempt"]);
            Assert.DoesNotContain(sample.Tags.Values.OfType<string>(), value => value.Contains("private", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("board_sharing")]
    public async Task ARCH_08_Recovered_factory_records_later_request_using_same_telemetry_service(string area)
    {
        using var factory = new BusinessMeterFactory(area, "factory");
        using var capture = new BusinessMetricCapture(factory.Meter, "healthy");
        var context = Context(area); var calls = 0;
        RequestDelegate next = _ => { calls++; return Task.CompletedTask; };
        Func<Task> invoke;
        if (area == "organization")
        {
            var telemetry = new OrganizationTelemetry(factory); var middleware = new OrganizationTelemetryMiddleware(next);
            invoke = () => middleware.InvokeAsync(context, telemetry);
        }
        else
        {
            var telemetry = new BoardSharingTelemetry(factory); var middleware = new BoardSharingTelemetryMiddleware(next);
            invoke = () => middleware.InvokeAsync(context, telemetry);
        }
        await invoke(); Assert.Empty(capture.Samples);
        factory.Fail = false;
        await invoke(); Assert.Equal(2, calls); Assert.Equal(2, capture.Samples.Count);
    }

    private static DefaultHttpContext Context(string area)
    {
        var context = new DefaultHttpContext(); context.Request.Method = area == "organization" ? "POST" : "GET";
        context.Request.Path = "/private-resource"; context.Request.QueryString = new("?token=private-query");
        context.Request.Headers.Authorization = "Bearer private-credential";
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        context.TraceIdentifier = "private-correlation";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(area == "organization" ? "/organizations/" : "/boards/{boardId:guid}"),
            0, EndpointMetadataCollection.Empty, "private-display-name"));
        return context;
    }
}

internal sealed class BusinessMeterFactory : IMeterFactory
{
    public Meter Meter { get; }
    public bool Fail { get; set; }
    public BusinessMeterFactory(string area, string fault)
    {
        Meter = new(area == "organization" ? OrganizationTelemetry.MeterName : BoardSharingTelemetry.MeterName);
        Fail = fault == "factory";
    }
    public Meter Create(MeterOptions options)
    {
        Assert.Equal(Meter.Name, options.Name);
        if (Fail) throw new InvalidOperationException("private-instrument-factory");
        return Meter;
    }
    public void Dispose() => Meter.Dispose();
}

internal sealed class BusinessMetricCapture : IDisposable
{
    private readonly MeterListener listener = new();
    public List<(string Name, Dictionary<string, object?> Tags)> Samples { get; } = [];
    public BusinessMetricCapture(Meter meter, string fault)
    {
        listener.InstrumentPublished = (instrument, observer) =>
        {
            if (!ReferenceEquals(instrument.Meter, meter)) return;
            if (fault.StartsWith("publish_", StringComparison.Ordinal) && instrument.Name.EndsWith("." + fault[8..], StringComparison.Ordinal))
                throw new InvalidOperationException("private-instrument-publication");
            observer.EnableMeasurementEvents(instrument);
        };
        void Add(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Samples.Add((instrument.Name, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value)));
            if (fault.StartsWith("measure_", StringComparison.Ordinal) && instrument.Name.EndsWith("." + fault[8..], StringComparison.Ordinal))
                throw new InvalidOperationException("private-instrument-measurement");
        }
        listener.SetMeasurementEventCallback<long>((i, _, tags, _) => Add(i, tags));
        listener.SetMeasurementEventCallback<double>((i, _, tags, _) => Add(i, tags));
        listener.Start();
    }
    public void Dispose() => listener.Dispose();
}

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("organization")]
    [InlineData("board_sharing")]
    public async Task ARCH_08_Throwing_business_meter_factory_preserves_core_HTTP_work_and_authorization(string area)
    {
        using var factory = new BusinessMeterFactory(area, "factory");
        await using var app = new ApiFactory(configureServices: services =>
        {
            if (area == "organization") services.AddSingleton(new OrganizationTelemetry(factory));
            else services.AddSingleton(new BoardSharingTelemetry(factory));
        });
        using var owner = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner);
        var ct = TestContext.Current.CancellationToken;
        var board = await TelemetryBoard(owner, ct);
        var view = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal(board, view.GetProperty("board").GetProperty("id").GetGuid());
        using var denied = await anonymous.GetAsync($"/boards/{board}", ct);
        // Board reads admit anonymous public access but conceal private Boards.
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("board_not_found", problem.GetProperty("code").GetString());
        Assert.DoesNotContain("private-instrument", await denied.Content.ReadAsStringAsync(ct));
        using var members = await anonymous.GetAsync($"/boards/{board}/members", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, members.StatusCode);
        using var health = await owner.GetAsync("/api/health", ct); Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
