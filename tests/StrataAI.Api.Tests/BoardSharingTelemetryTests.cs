using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_16_Global_search_metrics_measure_outcomes_without_query_names_or_cursor_material()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(owner);
        using var capture = new SharingMetricCapture(app.Services.GetRequiredService<BoardSharingTelemetry>().Meter);
        using var searched = await owner.GetAsync("/search?q=private-search-body&label=private-label&member=private-person", ct);
        Assert.Equal(HttpStatusCode.OK, searched.StatusCode);
        using var invalid = await owner.GetAsync("/search?q=private-search-body&after=private-cursor-material", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var denied = await anonymous.GetAsync("/search?q=private-search-body", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var samples = await capture.WaitAsync(3, ct); AssertSafeSharingSamples(samples, 3);
        Assert.All(samples, sample => Assert.Equal("global_search", sample.Tags["operation"]));
        Assert.Contains(samples, sample => sample.Tags["outcome"] as string == "success");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "invalid_search");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "unauthenticated");
        Assert.DoesNotContain(samples.SelectMany(sample => sample.Tags.Values.OfType<string>()), value => value.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Board_sharing_metrics_capture_response_outcomes_without_private_or_unbounded_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        using var anonymous = app.CreateClient(); await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var board = await TelemetryBoard(owner, ct);
        var user = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var capture = new SharingMetricCapture(app.Services.GetRequiredService<BoardSharingTelemetry>().Meter);
        using var directory = await owner.GetAsync($"/boards/{board}/members", ct); Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        using var saved = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "ORGANIZATION", version = 1 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var conflict = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/members/{user}", new { role = "private-email@example.test" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var invalidKey = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 2 }, "private-retry-material");
        Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
        using var unauthenticated = await anonymous.GetAsync($"/boards/{board}/members", ct); Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var denied = await outsider.GetAsync($"/boards/{board}/members", ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var samples = await capture.WaitAsync(7, ct);
        AssertSafeSharingSamples(samples, 7);
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "member_read" && sample.Tags["error_code"] as string == "board_not_found");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "unauthenticated");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "version_conflict" && sample.Tags["outcome"] as string == "conflict");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "invalid_board_role");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "invalid_idempotency_key");
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "visibility_change" && sample.Tags["outcome"] as string == "success"
            && sample.Tags["keyed_attempt"] is true);
    }

    [Fact]
    public async Task Board_invitation_metrics_measure_bound_administration_without_recipient_or_proof_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var capture = new SharingMetricCapture(app.Services.GetRequiredService<BoardSharingTelemetry>().Meter);
        using var created = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/invitations", new { email = "private-recipient@example.test", role = "MEMBER" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitation = await created.Content.ReadFromJsonAsync<JsonElement>(ct); var id = invitation.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrEmpty(invitation.GetProperty("invitationToken").GetString()));
        using var history = await owner.GetAsync($"/boards/{board}/invitations", ct); Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var revoked = await Mutate(owner, HttpMethod.Delete, $"/boards/{board}/invitations/{id}", new { }); Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/invitations", new { email = "not an email", role = "MEMBER" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var samples = await capture.WaitAsync(4, ct); AssertSafeSharingSamples(samples, 4);
        foreach (var operation in new[] { "invitation_create", "invitation_read", "invitation_revoke" })
            Assert.Contains(samples, sample => sample.Tags["operation"] as string == operation && sample.Tags["outcome"] as string == "success");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "invalid_email");
    }

    [Fact]
    public async Task A_failing_metric_listener_cannot_change_a_committed_board_visibility_response()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        var meter = app.Services.GetRequiredService<BoardSharingTelemetry>().Meter;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (ReferenceEquals(instrument.Meter, meter)) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("Disposable failing listener"));
        listener.Start();
        using var changed = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 1 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var acknowledgment = await changed.Content.ReadFromJsonAsync<JsonElement>(ct); Assert.Equal("PUBLIC", acknowledgment.GetProperty("visibility").GetString());
        var refreshed = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal("PUBLIC", refreshed.GetProperty("board").GetProperty("visibility").GetString());
        Assert.Equal(acknowledgment.GetProperty("version").GetInt64(), refreshed.GetProperty("board").GetProperty("version").GetInt64());
    }

    private static async Task<Guid> TelemetryBoard(HttpClient owner, CancellationToken ct)
    {
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Private Organization metrics fixture" });
        Assert.Equal(HttpStatusCode.Created, organization.StatusCode);
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var board = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Never label this private Board title", visibility = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, board.StatusCode);
        return (await board.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    private static void AssertSafeSharingSamples(SharingSample[] samples, int requests)
    {
        Assert.Equal(requests, samples.Count(sample => sample.Name == "strataai.board_sharing.requests"));
        Assert.Equal(requests, samples.Count(sample => sample.Name == "strataai.board_sharing.duration"));
        foreach (var sample in samples)
        {
            Assert.True(double.IsFinite(sample.Value) && sample.Value >= 0);
            Assert.Equal(new[] { "error_code", "keyed_attempt", "operation", "outcome" }, sample.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.IsType<bool>(sample.Tags["keyed_attempt"]);
            foreach (var value in sample.Tags.Values.OfType<string>())
            {
                Assert.Matches("^[a-z_]+$", value); Assert.InRange(value.Length, 1, 40);
            }
        }
    }

    private sealed record SharingSample(string Name, double Value, Dictionary<string, object?> Tags);
    private sealed class SharingMetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<SharingSample> _samples = new();
        public SharingMetricCapture(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) => { if (ReferenceEquals(instrument.Meter, meter)) listener.EnableMeasurementEvents(instrument); };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.Start();
        }
        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            _samples.Enqueue(new(instrument.Name, value, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
        public async Task<SharingSample[]> WaitAsync(int requests, CancellationToken ct)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var samples = _samples.ToArray();
                if (samples.Count(sample => sample.Name == "strataai.board_sharing.requests") >= requests
                    && samples.Count(sample => sample.Name == "strataai.board_sharing.duration") >= requests) return samples;
                await Task.Delay(10, ct);
            }
            return _samples.ToArray();
        }
        public void Dispose() => _listener.Dispose();
    }
}
