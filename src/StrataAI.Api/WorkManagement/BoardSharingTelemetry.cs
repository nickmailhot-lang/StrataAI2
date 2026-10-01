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
            ("/boards/{boardId:guid}/members", "GET") => "member_read",
            ("/boards/{boardId:guid}/members/{targetUserId:guid}", "PATCH") => "member_role",
            ("/boards/{boardId:guid}/members/{targetUserId:guid}", "DELETE") => "member_remove",
            ("/boards/{boardId:guid}/visibility", "PATCH") => "visibility_change",
            ("/boards/{boardId:guid}/invitations", "GET") => "invitation_read",
            ("/boards/{boardId:guid}/invitations", "POST") => "invitation_create",
            ("/boards/{boardId:guid}/invitations/{invitationId:guid}", "DELETE") => "invitation_revoke",
            _ => null,
        };

    internal static void SetError(HttpContext context, string? code)
    {
        context.Items[ErrorKey] = code switch
        {
            "board_not_found" or "organization_not_found" or "member_not_found" or "invitation_not_found"
                or "session_unavailable" or "work_storage_unavailable" or "invitation_storage_unavailable"
                or "invalid_visibility" or "invalid_board_role" or "invalid_member_version"
                or "invalid_board_member_cursor" or "invalid_invitation_cursor" or "invalid_email"
                or "invalid_invitation_role" or "member_not_eligible" or "sole_board_admin"
                or "version_conflict" or "idempotency_key_reused" or "idempotency_key_expired"
                or "invalid_idempotency_key" or "insufficient_permission" => code,
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
