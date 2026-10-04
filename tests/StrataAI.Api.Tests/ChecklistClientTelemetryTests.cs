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
    [Fact]
    public async Task Client_telemetry_rate_limit_is_partitioned_by_authenticated_user()
    {
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var payload = new { events = new[] { new { action = "read", kind = "use", count = 1 } } };
        for (var index = 0; index < 64; index++)
        {
            using var accepted = await Mutate(owner, HttpMethod.Post, "/me/checklist-client-events", payload);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }
        using var rejected = await Mutate(owner, HttpMethod.Post, "/me/checklist-client-events", payload);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using var activityRejected = await Mutate(owner, HttpMethod.Post, "/me/activity-client-events",
            new { events = new[] { new { action = "card_read", kind = "use", count = 1 } } });
        Assert.Equal(HttpStatusCode.TooManyRequests, activityRejected.StatusCode);
        using var independent = await Mutate(other, HttpMethod.Post, "/me/checklist-client-events", payload);
        Assert.Equal(HttpStatusCode.NoContent, independent.StatusCode);
    }
    [Fact]
    public async Task Checklist_client_observations_are_authenticated_bounded_and_have_only_fixed_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var anonymous = app.CreateClient(); await RegisterAndLogin(owner);
        var meter = app.Services.GetRequiredService<ChecklistClientTelemetry>().Meter;
        var counters = new ConcurrentQueue<(long Value, Dictionary<string, object?> Tags)>();
        var durations = new ConcurrentQueue<double>(); using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (ReferenceEquals(instrument.Meter, meter)) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => counters.Enqueue((value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Enqueue(value)); listener.Start();
        var payload = new { events = new object[] { new { action = "disclosure", kind = "open", count = 3 },
            new { action = "item_update", kind = "retry", count = 1 }, new { action = "item_update", kind = "success", count = 1, durationMs = 125d } } };
        using var denied = await Mutate(anonymous, HttpMethod.Post, "/me/checklist-client-events", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Empty(counters);
        using var accepted = await Mutate(owner, HttpMethod.Post, "/me/checklist-client-events", payload);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode); Assert.Equal(3, counters.Count);
        Assert.Equal(5, counters.Sum(sample => sample.Value)); Assert.Equal(.125, Assert.Single(durations));
        foreach (var sample in counters)
        {
            Assert.Equal(new[] { "action", "kind" }, sample.Tags.Keys.Order().ToArray());
            Assert.All(sample.Tags.Values, value => Assert.DoesNotContain("private", Assert.IsType<string>(value)));
        }
        using var csrf = await owner.PostAsync("/me/checklist-client-events", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode); Assert.Equal(3, counters.Count);
    }

    [Theory]
    [InlineData("{\"events\":[]}")]
    [InlineData("{\"events\":[{\"action\":\"private-card-id\",\"kind\":\"open\",\"count\":1}]}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"exception\",\"count\":1,\"message\":\"private-content\"}]}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"open\",\"count\":\"1\"}]}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"success\",\"count\":1,\"durationMs\":\"125\"}]}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"success\",\"count\":2,\"durationMs\":125}]}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"open\",\"count\":101}]}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"open\",\"count\":1,\"count\":2}]}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"open\",\"count\":1}],\"tenantId\":\"private\"}")]
    [InlineData("{\"events\":[{\"action\":\"read\",\"kind\":\"open\",\"count\":1},{\"action\":\"read\",\"kind\":\"private-error\",\"count\":1}]}")]
    public async Task Invalid_client_batches_are_rejected_before_any_measurement(string payload)
    {
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var meter = app.Services.GetRequiredService<ChecklistClientTelemetry>().Meter; var count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (ReferenceEquals(instrument.Meter, meter)) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref count)); listener.Start();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/me/checklist-client-events") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-StrataAI-Request", "1"); using var response = await owner.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, count);
    }

    [Fact]
    public async Task Client_telemetry_rejects_oversized_bodies_and_listener_failure_does_not_change_response()
    {
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/me/checklist-client-events") { Content = new StringContent(new string(' ', 8193)) };
        request.Headers.Add("X-StrataAI-Request", "1"); using var rejected = await owner.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        var meter = app.Services.GetRequiredService<ChecklistClientTelemetry>().Meter; using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (ReferenceEquals(instrument.Meter, meter)) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("Disposable listener failure")); listener.Start();
        using var accepted = await Mutate(owner, HttpMethod.Post, "/me/checklist-client-events", new { events = new[] { new { action = "disclosure", kind = "open", count = 1 } } });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }
}
