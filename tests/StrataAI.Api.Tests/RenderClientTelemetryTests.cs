using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("application_root_exception")]
    [InlineData("application_recovery_exception")]
    [InlineData("application_event_exception")]
    [InlineData("application_promise_exception")]
    [InlineData("application_render")]
    [InlineData("board_render")]
    [InlineData("card_render")]
    public async Task PRD_06_Render_observations_accept_only_private_free_exception_counts(string action)
    {
        await using var app = new ApiFactory(); using var actor = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(actor);
        var meter = app.Services.GetRequiredService<ActivityClientTelemetry>().Meter;
        var measurements = new ConcurrentQueue<(long Count, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, capture) => { if (ReferenceEquals(instrument.Meter, meter)) capture.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, count, tags, _) => measurements.Enqueue((count, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value))));
        listener.Start();
        var payload = new { events = new[] { new { action, kind = "exception", count = 2 } } };
        using var denied = await Mutate(anonymous, HttpMethod.Post, "/me/activity-client-events", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Empty(measurements);
        using var accepted = await Mutate(actor, HttpMethod.Post, "/me/activity-client-events", payload);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        var measurement = Assert.Single(measurements); Assert.Equal(2, measurement.Count);
        Assert.Equal(new Dictionary<string, object?> { ["action"] = action, ["kind"] = "exception" }, measurement.Tags);
        foreach (var field in new[] { "message", "stack", "componentStack", "path", "url", "boardId", "userId", "organizationId", "key", "durationMs" })
        {
            var invalid = new { events = new object[] { new { action, kind = "exception", count = 1 },
                new Dictionary<string, object> { ["action"] = action, ["kind"] = "exception", ["count"] = 1, [field] = field == "durationMs" ? 125 : "private-material" } } };
            using var refused = await Mutate(actor, HttpMethod.Post, "/me/activity-client-events", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode); Assert.Single(measurements);
        }
        using var wrongKind = await Mutate(actor, HttpMethod.Post, "/me/activity-client-events", new { events = new[] { new { action, kind = "success", count = 1, durationMs = 125 } } });
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode); Assert.Single(measurements);
        using var csrf = await actor.PostAsync("/me/activity-client-events", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode); Assert.Single(measurements);
    }
}
