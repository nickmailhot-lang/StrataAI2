using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_03_Organization_metrics_measure_real_success_denial_validation_conflict_and_keyed_recovery_without_private_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner);
        var meter = app.Services.GetRequiredService<OrganizationTelemetry>().Meter;
        var samples = new ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, capture) => { if (ReferenceEquals(instrument.Meter, meter)) capture.EnableMeasurementEvents(instrument); };
        void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            samples.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value)));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags)); listener.Start();
        using var invalid = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "private-metric-fixture-name" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var path = $"/organizations/{org}";
        using var read = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var version = (await read.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("version").GetInt64();
        using var denied = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var conflict = await Mutate(owner, HttpMethod.Patch, path, new { name = "private-metric-fixture-name", version = version + 10 });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var key = Guid.NewGuid().ToString(); var input = new { name = "private-metric-fixture-updated", description = "private-metric-fixture-body", version };
        using var update = await Mutate(owner, HttpMethod.Patch, path, input, key); Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Patch, path, input, key); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        for (var attempt = 0; attempt < 100 && samples.Count < 14; attempt++) await Task.Delay(10, ct);
        var observed = samples.ToArray(); Assert.Equal(14, observed.Length);
        Assert.Equal(7, observed.Count(row => row.Name == "strataai.organization.requests"));
        Assert.Equal(7, observed.Count(row => row.Name == "strataai.organization.duration"));
        Assert.All(observed, row => {
            Assert.True(double.IsFinite(row.Value) && row.Value >= 0);
            Assert.Equal(new[] { "error_code", "keyed_attempt", "operation", "outcome" }, row.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.IsType<bool>(row.Tags["keyed_attempt"]);
            foreach (var value in row.Tags.Values.OfType<string>()) { Assert.Matches("^[a-z_]+$", value); Assert.InRange(value.Length, 1, 40); }
        });
        Assert.Contains(observed, row => row.Tags["error_code"] as string == "invalid_organization_name");
        Assert.Contains(observed, row => row.Tags["error_code"] as string == "version_conflict");
        Assert.Contains(observed, row => row.Tags["outcome"] as string == "denied" && row.Tags["error_code"] as string == "unauthenticated");
        Assert.Equal(2, observed.Count(row => row.Name == "strataai.organization.requests" && row.Tags["operation"] as string == "update"
            && row.Tags["outcome"] as string == "success" && row.Tags["keyed_attempt"] is true));
    }
}
