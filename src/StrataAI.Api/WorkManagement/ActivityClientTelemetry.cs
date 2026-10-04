using System.Diagnostics.Metrics;
using System.Text.Json;

namespace StrataAI.Api.WorkManagement;

// Client counts are untrusted observations, never immutable business history.
public sealed class ActivityClientTelemetry
{
    public const string MeterName = "StrataAI.ActivityClient";
    private static readonly HashSet<string> Actions = ["board_read", "card_read", "board_disclosure", "card_disclosure",
        "comment_disclosure", "comment_read", "comment_create", "comment_edit", "comment_delete",
        "mention_read", "mention_selection", "card_group_confirmation", "board_group_confirmation", "search_disclosure", "search_read"];
    public Meter Meter { get; }
    private readonly Counter<long> _events;
    private readonly Histogram<double> _duration;
    public ActivityClientTelemetry(IMeterFactory factory)
    {
        Meter = factory.Create(MeterName);
        _events = Meter.CreateCounter<long>("strataai.activity.client.events", "{event}");
        _duration = Meter.CreateHistogram<double>("strataai.activity.client.duration", "s");
    }
    public static IReadOnlyList<ChecklistClientTelemetry.Observation>? Parse(JsonElement root) =>
        ChecklistClientTelemetry.Parse(root, Actions);
    public void Record(IReadOnlyList<ChecklistClientTelemetry.Observation> observations)
    {
        foreach (var observation in observations)
        {
            KeyValuePair<string, object?>[] tags = [new("action", observation.Action), new("kind", observation.Kind)];
            try { _events.Add(observation.Count, tags); } catch { /* Operator listeners never affect product behavior. */ }
            if (observation.DurationMs is double duration)
                try { _duration.Record(duration / 1000, tags); } catch { /* Best effort only. */ }
        }
    }
}
public static class ActivityClientTelemetryEndpoints
{
    public static void MapActivityClientTelemetry(this WebApplication app) =>
        app.MapPost("/me/activity-client-events", async (HttpContext context, ActivityClientTelemetry telemetry) =>
            await ChecklistClientTelemetryEndpoints.Read(context, ActivityClientTelemetry.Parse, telemetry.Record))
            .RequireAuthorization().RequireRateLimiting("client-events");
}
