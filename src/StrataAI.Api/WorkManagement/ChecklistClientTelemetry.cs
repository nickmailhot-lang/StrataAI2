using System.Diagnostics.Metrics;
using System.Text.Json;

namespace StrataAI.Api.WorkManagement;

// Aggregate client observations are untrusted telemetry, never business audit.
// The protocol deliberately has no identity, scope, content or exception fields.
public sealed class ChecklistClientTelemetry(IMeterFactory factory)
{
    public const string MeterName = "StrataAI.ChecklistClient";
    public Meter Meter { get; } = factory.Create(MeterName);
    private Counter<long>? _events;
    private Histogram<double>? _duration;
    private static readonly HashSet<string> Actions = ["read", "item_read", "create", "rename", "delete", "position",
        "item_create", "item_update", "item_delete", "item_position", "disclosure", "item_disclosure", "realtime"];
    private static readonly HashSet<string> Kinds = ["open", "use", "retry", "exception", "conflict", "reconnect", "success", "failure"];
    public sealed record Observation(string Action, string Kind, int Count, double? DurationMs);
    public static IReadOnlyList<Observation>? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
            || !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array
            || events.GetArrayLength() is < 1 or > 20) return null;
        var result = new List<Observation>();
        foreach (var entry in events.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) return null;
            var names = new HashSet<string>();
            foreach (var property in entry.EnumerateObject())
                if (!names.Add(property.Name) || property.Name is not ("action" or "kind" or "count" or "durationMs")) return null;
            if (!entry.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String
                || !Actions.Contains(action.GetString()!) || !entry.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                || !Kinds.Contains(kind.GetString()!) || !entry.TryGetProperty("count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var number)
                || number is < 1 or > 100) return null;
            double? duration = null;
            if (kind.GetString() is "success" or "failure")
            {
                if (number != 1 || !entry.TryGetProperty("durationMs", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var ms)
                    || !double.IsFinite(ms) || ms is < 0 or > 60000) return null;
                duration = ms;
            }
            else if (entry.TryGetProperty("durationMs", out _)) return null;
            result.Add(new(action.GetString()!, kind.GetString()!, number, duration));
        }
        return result;
    }
    public void Record(IReadOnlyList<Observation> observations)
    {
        _events ??= Meter.CreateCounter<long>("strataai.checklist.client.events", "{event}");
        _duration ??= Meter.CreateHistogram<double>("strataai.checklist.client.duration", "s");
        foreach (var observation in observations)
        {
            KeyValuePair<string, object?>[] tags = [new("action", observation.Action), new("kind", observation.Kind)];
            try { _events.Add(observation.Count, tags); } catch { /* Listener failures never affect the product. */ }
            if (observation.DurationMs is double duration)
                try { _duration.Record(duration / 1000, tags); } catch { /* Best-effort operator observations. */ }
        }
    }
}

public static class ChecklistClientTelemetryEndpoints
{
    public static void MapChecklistClientTelemetry(this WebApplication app)
    {
        app.MapPost("/me/checklist-client-events", async (HttpContext context, ChecklistClientTelemetry telemetry) =>
        {
            if (context.Request.ContentLength is > 8192) return Results.StatusCode(413);
            var bytes = new byte[8193]; var length = 0;
            while (length < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(bytes.AsMemory(length), context.RequestAborted);
                if (read == 0) break;
                length += read;
            }
            if (length > 8192) return Results.StatusCode(413);
            try
            {
                using var document = JsonDocument.Parse(bytes.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 5 });
                var observations = ChecklistClientTelemetry.Parse(document.RootElement);
                if (observations is null) return Results.BadRequest();
                telemetry.Record(observations);
                return Results.NoContent();
            }
            catch (JsonException) { return Results.BadRequest(); }
        }).RequireAuthorization().RequireRateLimiting("client-events");
    }
}
