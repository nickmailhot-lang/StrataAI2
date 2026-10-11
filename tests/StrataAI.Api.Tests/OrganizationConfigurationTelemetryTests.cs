using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("organization_configuration_disclosure")]
    [InlineData("organization_configuration_read")]
    [InlineData("organization_configuration_change")]
    [InlineData("organization_configuration_history")]
    [InlineData("organization_configuration_activity")]
    public void PRD_27_configuration_telemetry_parser_rejects_private_fields_atomically(string action)
    {
        using var valid = JsonDocument.Parse(JsonSerializer.Serialize(new { events = new object[] {
            new { action, kind = "retry", count = 1 }, new { action, kind = "failure", count = 1, durationMs = 125 } } }));
        Assert.Equal(2, ActivityClientTelemetry.Parse(valid.RootElement)!.Count);
        foreach (var field in new[] { "legalName", "jurisdiction", "actorId", "organizationId", "eventId", "correlationId", "cursor", "key", "body", "diagnostic" })
        {
            using var invalid = JsonDocument.Parse(JsonSerializer.Serialize(new { events = new object[] {
                new { action, kind = "use", count = 1 },
                new Dictionary<string, object> { ["action"] = action, ["kind"] = "retry", ["count"] = 1, [field] = "private-value" } } }));
            Assert.Null(ActivityClientTelemetry.Parse(invalid.RootElement));
        }
    }

    [Fact]
    public async Task PRD_27_configuration_telemetry_HTTP_requires_session_and_records_only_valid_fixed_batches()
    {
        await using var app = new ApiFactory(); using var actor = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(actor);
        var meter = app.Services.GetRequiredService<ActivityClientTelemetry>().Meter;
        var counters = new ConcurrentQueue<Dictionary<string, object?>>();
        var durations = new ConcurrentQueue<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, capture) => { if (ReferenceEquals(instrument.Meter, meter)) capture.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => counters.Enqueue(tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value)));
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => {
            Assert.Equal(new[] { "action", "kind" }, tags.ToArray().Select(pair => pair.Key).Order().ToArray()); durations.Enqueue(value); });
        listener.Start();
        var payload = new { events = new object[] {
            new { action = "organization_configuration_disclosure", kind = "open", count = 1 },
            new { action = "organization_configuration_read", kind = "reconnect", count = 1 },
            new { action = "organization_configuration_change", kind = "retry", count = 1 },
            new { action = "organization_configuration_history", kind = "use", count = 1 },
            new { action = "organization_configuration_activity", kind = "use", count = 1 },
            new { action = "organization_configuration_change", kind = "success", count = 1, durationMs = 125 } } };
        using var denied = await Mutate(anonymous, HttpMethod.Post, "/me/activity-client-events", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Empty(counters); Assert.Empty(durations);
        using var accepted = await Mutate(actor, HttpMethod.Post, "/me/activity-client-events", payload);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode); Assert.Equal(6, counters.Count); Assert.Equal(0.125, Assert.Single(durations));
        Assert.All(counters, tags => Assert.Equal(new[] { "action", "kind" }, tags.Keys.Order().ToArray()));
        using var rejected = await Mutate(actor, HttpMethod.Post, "/me/activity-client-events", new { events = new object[] {
            new { action = "organization_configuration_read", kind = "use", count = 1 },
            new { action = "organization_configuration_change", kind = "retry", count = 1, key = Guid.NewGuid(), legalName = "private-value" } } });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Equal(6, counters.Count); Assert.Single(durations);
    }
}
