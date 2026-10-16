using System.Diagnostics.Metrics;
using System.Text.Json;

namespace StrataAI.Api.WorkManagement;

// Client counts are untrusted observations, never immutable business history.
public sealed class ActivityClientTelemetry(IMeterFactory factory)
{
    public const string MeterName = "StrataAI.ActivityClient";
    private static readonly HashSet<string> Actions = ["application_root_exception", "application_recovery_exception", "application_event_exception", "application_promise_exception", "application_render", "board_render", "card_render", "board_read", "card_read", "board_disclosure", "card_disclosure",
        "comment_disclosure", "comment_read", "comment_create", "comment_edit", "comment_delete",
        "mention_read", "mention_selection", "card_group_confirmation", "board_group_confirmation", "search_disclosure", "search_read",
        "notification_disclosure", "notification_read", "notification_mark_read", "watch_disclosure", "watch_read", "watch_change", "archive_list_disclosure", "archive_list_read", "archive_list_restore", "archive_list_delete",
        "archive_card_disclosure", "archive_card_read", "archive_card_restore", "archive_card_delete", "list_archive", "card_archive", "archive_board_disclosure", "archive_board_read", "archive_board_restore", "archive_board_delete", "board_archive", "board_metadata_update", "board_star_disclosure", "board_star_read", "board_star_change", "board_copy_disclosure", "board_copy_read", "board_copy_change", "navigation_context", "navigation_board", "navigation_card",
        "organization_settings_disclosure", "organization_settings_read", "organization_settings_update",
        "organization_creation_disclosure", "organization_creation",
        "organization_members_disclosure", "organization_members_read", "organization_member_review", "organization_member_remove",
        "organization_departure_disclosure", "organization_departure_read", "organization_departure_change",
        "organization_deletion_disclosure", "organization_deletion_read", "organization_deletion_change", "organization_deletion_status", "organization_deletion_completion",
        "organization_invitation_disclosure", "organization_invitation_read", "organization_invitation_create",
        "board_invitation_disclosure", "board_invitation_read", "board_invitation_create",
        "organization_invitation_history_disclosure", "organization_invitation_history_read", "organization_invitation_history_revoke", "board_invitation_history_disclosure", "board_invitation_history_read", "board_invitation_history_revoke",
        "invitation_recipient_disclosure", "invitation_recipient_read", "invitation_recipient_accept", "invitation_link_disclosure", "invitation_link_review", "invitation_link_accept",
        "organization_configuration_disclosure", "organization_configuration_read", "organization_configuration_change",
        "organization_configuration_history", "organization_configuration_activity",
        "organization_lifecycle_disclosure", "organization_lifecycle_read", "organization_lifecycle_change"];
    public Meter Meter => factory.Create(MeterName);
    private Counter<long>? _events;
    private Histogram<double>? _duration;
    public static IReadOnlyList<ChecklistClientTelemetry.Observation>? Parse(JsonElement root)
    {
        var observations = ChecklistClientTelemetry.Parse(root, Actions);
        if (observations is null || observations.Any(observation => (observation.Action.EndsWith("_render", StringComparison.Ordinal) || observation.Action.EndsWith("_exception", StringComparison.Ordinal))
            && (observation.Kind != "exception" || observation.DurationMs is not null))) return null;
        return observations;
    }
    public void Record(IReadOnlyList<ChecklistClientTelemetry.Observation> observations)
    {
        Meter meter;
        try { meter = Meter; }
        catch { return; /* Optional observations cannot change client-event admission. */ }
        foreach (var observation in observations)
        {
            KeyValuePair<string, object?>[] tags = [new("action", observation.Action), new("kind", observation.Kind)];
            try { (_events ??= meter.CreateCounter<long>("strataai.activity.client.events", "{event}")).Add(observation.Count, tags); }
            catch { /* Publication/listener failures never affect product behavior. */ }
            if (observation.DurationMs is double duration)
                try { (_duration ??= meter.CreateHistogram<double>("strataai.activity.client.duration", "s")).Record(duration / 1000, tags); }
                catch { /* Independent best-effort latency observation. */ }
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
