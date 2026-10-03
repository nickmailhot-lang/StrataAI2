using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace StrataAI.Api.WorkManagement;

// Operator-only native instruments. No request content, identity, tenant/object
// identifiers, route values, correlation IDs or retry keys become metric labels.
public sealed class BoardSharingTelemetry
{
    public const string MeterName = "StrataAI.BoardSharing";
    public Meter Meter { get; }
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _duration;
    private static readonly object ErrorKey = new();
    public BoardSharingTelemetry(IMeterFactory factory)
    {
        Meter = factory.Create(MeterName);
        _requests = Meter.CreateCounter<long>("strataai.board_sharing.requests", "{request}");
        _duration = Meter.CreateHistogram<double>("strataai.board_sharing.duration", "s");
    }

    internal static string? Operation(HttpContext context) =>
        ((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText, context.Request.Method.ToUpperInvariant()) switch
        {
            ("/boards/{boardId:guid}", "GET") => "board_read",
            ("/watch/{entityType}/{entityId:guid}", "GET") => "watch_read",
            ("/watch/{entityType}/{entityId:guid}", "PUT") => "watch_create",
            ("/watch/{entityType}/{entityId:guid}", "DELETE") => "watch_remove",
            ("/organizations/{organizationId:guid}/notifications", "GET") => "notification_read",
            ("/organizations/{organizationId:guid}/notifications/{notificationId:guid}/read", "POST") => "notification_mark_read",
            ("/organizations/{organizationId:guid}/notifications/read", "POST") => "notification_bulk_read",
            ("/boards/{boardId:guid}/cards", "GET") => "board_filter_read",
            ("/boards/{boardId:guid}/assignable-members", "GET") => "assignable_member_read",
            ("/cards/{cardId:guid}/members", "GET") => "card_member_read",
            ("/cards/{cardId:guid}/attachments", "GET") => "attachment_read",
            ("/cards/{cardId:guid}/attachments", "POST") => "attachment_file_create",
            ("/cards/{cardId:guid}/attachment-upload-options", "GET") => "attachment_upload_options",
            ("/cards/{cardId:guid}/attachments/url", "POST") => "attachment_url_create",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}/items", "GET") => "checklist_item_read",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}/items", "POST") => "checklist_item_create",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}/items/{itemId:guid}", "PATCH") => "checklist_item_update",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}/position", "PATCH") => "checklist_position",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}/items/{itemId:guid}/position", "PATCH") => "checklist_item_position",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}/items/{itemId:guid}", "DELETE") => "checklist_item_delete",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}", "DELETE") => "checklist_delete",
            ("/cards/{cardId:guid}/checklists", "GET") => "checklist_read",
            ("/cards/{cardId:guid}/checklists", "POST") => "checklist_create",
            ("/cards/{cardId:guid}/checklists/{checklistId:guid}", "PATCH") => "checklist_rename",
            ("/cards/{cardId:guid}/member-options", "GET") => "card_member_options",
            ("/cards/{cardId:guid}/members/{userId:guid}", "PUT") => "card_member_assign",
            ("/cards/{cardId:guid}/members/{userId:guid}", "DELETE") => "card_member_remove",
            ("/boards/{boardId:guid}/labels", "GET") => "label_read",
            ("/boards/{boardId:guid}/labels", "POST") => "label_create",
            ("/labels/{labelId:guid}", "PATCH") => "label_update",
            ("/labels/{labelId:guid}", "DELETE") => "label_delete",
            ("/labels/{labelId:guid}/move", "POST") => "label_move",
            ("/cards/{cardId:guid}/labels", "GET") => "card_label_read",
            ("/cards/{cardId:guid}/label-options", "GET") => "card_label_options",
            ("/cards/{cardId:guid}/labels/{labelId:guid}", "PUT") => "card_label_assign",
            ("/cards/{cardId:guid}/labels/{labelId:guid}", "DELETE") => "card_label_remove",
            ("/boards/{boardId:guid}/members", "GET") => "member_read",
            ("/boards/{boardId:guid}/members/{targetUserId:guid}", "PATCH") => "member_role",
            ("/boards/{boardId:guid}/members/{targetUserId:guid}", "DELETE") => "member_remove",
            ("/boards/{boardId:guid}/visibility", "PATCH") => "visibility_change",
            ("/boards/{boardId:guid}/date-policy", "PATCH") => "board_date_policy",
            ("/boards/{boardId:guid}/invitations", "GET") => "invitation_read",
            ("/boards/{boardId:guid}/invitations", "POST") => "invitation_create",
            ("/boards/{boardId:guid}/invitations/{invitationId:guid}", "DELETE") => "invitation_revoke",
            ("/lists/{listId:guid}", "PATCH") => "list_update",
            ("/boards/{boardId:guid}/lists", "POST") => "list_create",
            ("/boards/{boardId:guid}/archived-lists", "GET") => "archived_list_read",
            ("/boards/{boardId:guid}/archived-cards", "GET") => "archived_card_read",
            ("/lists/{listId:guid}/archive", "POST") => "list_archive",
            ("/lists/{listId:guid}/restore", "POST") => "list_restore",
            ("/lists/{listId:guid}/copy", "POST") => "list_copy",
            ("/lists/{listId:guid}", "DELETE") => "list_delete",
            ("/lists/{listId:guid}/cards", "POST") => "card_create",
            ("/cards/{cardId:guid}", "PATCH") => "card_update",
            ("/cards/{cardId:guid}/dates", "PATCH") => "card_date_update",
            ("/cards/{cardId:guid}/reminders", "GET") => "card_reminder_read",
            ("/cards/{cardId:guid}/attachments/{attachmentId:guid}/download", "GET") => "attachment_download",
            ("/cards/{cardId:guid}/attachments/{attachmentId:guid}/preview", "GET") => "attachment_preview",
            ("/attachments/{attachmentId:guid}/preview", "GET") => "attachment_preview",
            ("/cards/{cardId:guid}/attachments/{attachmentId:guid}/preview-options", "GET") => "attachment_preview_options",
            ("/cards/{cardId:guid}/attachments/{attachmentId:guid}/download-options", "GET") => "attachment_download_options",
            ("/attachments/{attachmentId:guid}/download", "GET") => "attachment_download",
            ("/cards/{cardId:guid}/reminders", "POST") => "card_reminder_set",
            ("/cards/{cardId:guid}/reminders", "DELETE") => "card_reminder_cancel",
            ("/cards/{cardId:guid}/archive", "POST") => "card_archive",
            ("/cards/{cardId:guid}/restore", "POST") => "card_restore",
            ("/cards/{cardId:guid}", "DELETE") => "card_delete",
            ("/cards/{cardId:guid}/move", "POST") => "card_move",
            _ => null,
        };

    internal static void SetError(HttpContext context, string? code)
    {
        context.Items[ErrorKey] = code switch
        {
            "board_not_found" or "organization_not_found" or "member_not_found" or "invitation_not_found"
                or "notification_not_found" or "invalid_notification_cursor" or "invalid_notification_selection"
                or "watch_not_found" or "invalid_watch_version" or "invalid_card_dates" or "invalid_card_date_version" or "invalid_board_date_policy"
                or "card_reminder_not_found" or "invalid_card_reminder_version" or "invalid_card_reminder_interval"
                or "invalid_checklist_item_text" or "checklist_item_not_found" or "checklist_not_found" or "invalid_checklist_cursor" or "invalid_checklist_title" or "invalid_checklist_version"
                or "session_unavailable" or "work_storage_unavailable" or "invitation_storage_unavailable"
                or "invalid_attachment_cursor" or "invalid_attachment_version" or "invalid_attachment_url" or "invalid_attachment_upload"
                or "attachment_too_large" or "attachment_type_not_allowed" or "attachment_integrity_invalid" or "attachment_source_unavailable"
                or "attachment_upload_in_progress" or "attachment_upload_unavailable"
                or "invalid_visibility" or "invalid_board_role" or "invalid_member_version"
                or "invalid_board_member_cursor" or "invalid_invitation_cursor" or "invalid_email"
                or "invalid_invitation_role" or "member_not_eligible" or "sole_board_admin"
                or "version_conflict" or "idempotency_key_reused" or "idempotency_key_expired"
                or "invalid_idempotency_key" or "insufficient_permission"
                or "list_not_found" or "card_not_found" or "invalid_list_name" or "invalid_rank"
                or "invalid_move_position" or "rank_space_exhausted"
                or "invalid_lifecycle_transition" or "delete_confirmation_required"
                or "deletion_impact_required" or "deletion_impact_changed"
                or "invalid_archive_cursor" or "invalid_card_title"
                or "label_not_found" or "invalid_label_name" or "invalid_label_color" or "invalid_label_cursor" or "invalid_board_filter" or "invalid_card_member_version" => code,
            _ => "other_error",
        };
    }

    internal void Record(HttpContext context, string operation, double seconds, bool threw)
    {
        var status = threw ? 500 : context.Response.StatusCode;
        var outcome = context.RequestAborted.IsCancellationRequested ? "cancelled" : status switch
        {
            >= 200 and < 300 => "success", 401 or 403 or 404 => "denied",
            409 => "conflict", 429 => "limited", >= 500 => "unavailable", _ => "invalid",
        };
        var code = threw ? "server_error" : status < 400 ? "none" : context.Items[ErrorKey] as string
            ?? (status switch { 401 => "unauthenticated", 403 => "forbidden", 404 => "not_found",
                409 => "conflict", 429 => "rate_limited", >= 500 => "unavailable", _ => "invalid_request" });
        var keyed = context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) && keys.Count == 1
            && keys[0] is { Length: 36 } key && Guid.TryParseExact(key, "D", out var id) && id != Guid.Empty;
        TagList tags = new() { { "operation", operation }, { "outcome", outcome }, { "error_code", code }, { "keyed_attempt", keyed } };
        // A failing operator listener must never turn a committed action into an
        // application error or prevent its authoritative response being returned.
        try { _requests.Add(1, tags); _duration.Record(seconds, tags); }
        catch { /* Native listener failure is isolated from application behavior. */ }
    }
}

public sealed class BoardSharingTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, BoardSharingTelemetry telemetry)
    {
        var start = Stopwatch.GetTimestamp(); var threw = false;
        try { await next(context); }
        catch { threw = true; throw; }
        finally
        {
            // Routing has completed for matched requests. No raw path matching
            // is needed, and normal request duration includes security checks.
            var operation = BoardSharingTelemetry.Operation(context);
            if (operation is not null) telemetry.Record(context, operation, Stopwatch.GetElapsedTime(start).TotalSeconds, threw);
        }
    }
}

public sealed class BoardSharingResultFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (BoardSharingTelemetry.Operation(context.HttpContext) is not null && result is IValueHttpResult value
            && value.Value is ProblemDetails problem && problem.Extensions.TryGetValue("code", out var code))
            BoardSharingTelemetry.SetError(context.HttpContext, code as string);
        return result;
    }
}
