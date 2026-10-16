using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api;
using StrataAI.Api.Auth;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class OptionalMetricPublicationTests
{
    public static TheoryData<string, string> Cases
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var area in new[] { "checklist", "activity", "http", "authentication" })
            foreach (var fault in new[] { "healthy", "factory", "publish_counter", "publish_duration", "measure_counter", "measure_duration" })
                rows.Add(area, fault);
            return rows;
        }
    }

    internal static string Fault(string area, string fault) => fault.Replace("counter", area is "activity" or "checklist" ? "events" : "requests", StringComparison.Ordinal);
    internal static Action<double> Recorder(string area, BusinessMeterFactory factory)
    {
        if (area == "http")
        {
            var telemetry = new HttpServerTelemetry(factory);
            return seconds => telemetry.Record("GET", 200, "completed", seconds);
        }
        if (area == "authentication")
        {
            var telemetry = new AuthenticationTelemetry(factory);
            return seconds => telemetry.Record("login", 200, "completed", seconds);
        }
        if (area == "checklist")
        {
            var telemetry = new ChecklistClientTelemetry(factory);
            return seconds => telemetry.Record([new("read", "success", 1, seconds * 1000)]);
        }
        var activity = new ActivityClientTelemetry(factory);
        return seconds => activity.Record([new("card_read", "success", 1, seconds * 1000)]);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ARCH_08_All_API_metric_publication_is_optional_and_independent(string area, string fault)
    {
        using var factory = new BusinessMeterFactory(area, fault);
        using var capture = new BusinessMetricCapture(factory.Meter, Fault(area, fault));
        Assert.Null(Record.Exception(() =>
        {
            var record = Recorder(area, factory);
            record(.45); record(.75);
        }));
        var expected = fault == "factory" ? 0 : fault.StartsWith("publish_", StringComparison.Ordinal) ? 2 : 4;
        Assert.Equal(expected, capture.Samples.Count);
        if (fault == "publish_counter") Assert.All(capture.Samples, sample => Assert.EndsWith(".duration", sample.Name));
        if (fault == "publish_duration") Assert.All(capture.Samples, sample => Assert.DoesNotContain(".duration", sample.Name));
        foreach (var sample in capture.Samples)
        {
            Assert.DoesNotContain(sample.Tags.Values.OfType<string>(), value => value.Contains("private", StringComparison.Ordinal));
            if (sample.Name.EndsWith(".duration", StringComparison.Ordinal)) Assert.Contains(sample.Value, new[] { .45, .75 });
            else Assert.Equal(1, sample.Value);
        }
    }

    [Theory]
    [InlineData("checklist")]
    [InlineData("activity")]
    [InlineData("http")]
    [InlineData("authentication")]
    public void ARCH_08_Optional_metric_factory_can_recover_without_replacing_recorder(string area)
    {
        using var factory = new BusinessMeterFactory(area, "factory");
        using var capture = new BusinessMetricCapture(factory.Meter, "healthy");
        var record = Recorder(area, factory);
        Assert.Null(Record.Exception(() => record(.45))); Assert.Empty(capture.Samples);
        factory.Fail = false;
        record(.75); Assert.Equal(2, capture.Samples.Count);
    }
}

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("checklist", "factory")]
    [InlineData("checklist", "publish_counter")]
    [InlineData("checklist", "publish_duration")]
    [InlineData("activity", "factory")]
    [InlineData("activity", "publish_counter")]
    [InlineData("activity", "publish_duration")]
    public async Task ARCH_08_Optional_client_instruments_preserve_HTTP_admission_validation_and_health(string area, string fault)
    {
        using var factory = new BusinessMeterFactory(area, fault);
        using var capture = new BusinessMetricCapture(factory.Meter, OptionalMetricPublicationTests.Fault(area, fault));
        await using var app = new ApiFactory(configureServices: services =>
        {
            if (area == "checklist") services.AddSingleton(new ChecklistClientTelemetry(factory));
            else services.AddSingleton(new ActivityClientTelemetry(factory));
        });
        using var owner = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner);
        var ct = TestContext.Current.CancellationToken;
        var path = area == "checklist" ? "/me/checklist-client-events" : "/me/activity-client-events";
        var action = area == "checklist" ? "read" : "card_read";
        var payload = new { events = new[] { new { action, kind = "success", count = 1, durationMs = 450 } } };
        using var denied = await Mutate(anonymous, HttpMethod.Post, path, payload);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Post, path,
            new { events = new[] { new { action, kind = "use", count = 1, privateContent = "private-body" } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(capture.Samples);
        for (var index = 0; index < 2; index++)
        {
            using var accepted = await Mutate(owner, HttpMethod.Post, path, payload);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Empty(await accepted.Content.ReadAsByteArrayAsync(ct));
        }
        Assert.Equal(fault == "factory" ? 0 : 2, capture.Samples.Count);
        using var large = await Mutate(owner, HttpMethod.Post, path, new { privateContent = new string('x', 8193) });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        using var health = await owner.GetAsync("/api/health", ct); Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
