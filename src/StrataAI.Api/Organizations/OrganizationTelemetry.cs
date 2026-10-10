using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace StrataAI.Api.Organizations;

// Fixed operator aggregates only: no content, actor/tenant/object IDs, paths,
// correlation IDs or retry-key values enter labels. Audit remains authoritative.
public sealed class OrganizationTelemetry
{
    public const string MeterName = "StrataAI.Organizations";
    public Meter Meter { get; }
    private readonly Counter<long> requests;
    private readonly Histogram<double> duration;
    private static readonly object ErrorKey = new();
    public OrganizationTelemetry(IMeterFactory factory)
    {
        Meter = factory.Create(MeterName);
        requests = Meter.CreateCounter<long>("strataai.organization.requests", "{request}");
        duration = Meter.CreateHistogram<double>("strataai.organization.duration", "s");
    }
    internal static string? Operation(HttpContext context) =>
        ((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText, context.Request.Method.ToUpperInvariant()) switch
        {
            ("/organizations/", "POST") => "create",
            ("/organizations/", "GET") => "list",
            ("/organizations/directory", "GET") => "directory",
            ("/organizations/{organizationId:guid}", "GET") => "read",
            ("/organizations/{organizationId:guid}", "PATCH") => "update",
            ("/organizations/{organizationId:guid}", "DELETE") => "delete_request",
            ("/organizations/{organizationId:guid}/leave", "POST") => "leave",
            ("/organizations/{organizationId:guid}/members", "GET") => "members",
            ("/organizations/{organizationId:guid}/members/{targetUserId:guid}", "GET") => "member_review",
            ("/organizations/{organizationId:guid}/members/{targetUserId:guid}", "DELETE") => "member_remove",
            ("/organizations/{organizationId:guid}/boards", "GET") => "boards",
            ("/organizations/{organizationId:guid}/boards/directory", "GET") => "board_directory",
            ("/organizations/{organizationId:guid}/surface-access", "GET") => "surface_access",
            ("/organizations/{organizationId:guid}/deletion-requests/{requestId:guid}", "GET") => "delete_observation",
            ("/organizations/{organizationId:guid}/metadata-events", "GET") => "metadata_replay",
            ("/organizations/{organizationId:guid}/lifecycle-events", "GET") => "lifecycle_replay",
            ("/organizations/{organizationId:guid}/configuration", "GET") => "configuration_read",
            ("/organizations/{organizationId:guid}/configuration", "PATCH") => "configuration_change",
            ("/organizations/{organizationId:guid}/configuration/history", "GET") => "configuration_history",
            ("/organizations/{organizationId:guid}/configuration/intake-boards/{boardId:guid}/lists", "GET") => "configuration_intake_lists",
            _ => null,
        };
    internal static void SetError(HttpContext context, string? code) => context.Items[ErrorKey] = code switch
    {
        "invalid_idempotency_key" or "idempotency_conflict" or "idempotency_expired"
        or "invalid_member_version" or "member_version_conflict" or "invalid_board_directory_cursor"
        or "invalid_organization_cursor" or "invalid_member_cursor" or "session_unavailable"
        or "organization_storage_unavailable" or "invalid_organization_name" or "invalid_organization_logo_url"
        or "version_conflict" or "sole_owner" or "insufficient_permission" or "member_not_found"
        or "organization_not_found" or "invalid_sync_limit" or "organization_sync_unavailable" => code,
        "idempotency_key_required" or "idempotency_key_conflict" or "idempotency_key_expired"
        or "configuration_source_unavailable" or "configuration_identifier_unavailable"
        or "configuration_intake_unavailable" or "invalid_configuration_cursor"
        or "invalid_configuration_request" => code,
        { } value when value.StartsWith("invalid_configuration_", StringComparison.Ordinal) => "invalid_configuration_field",
        _ => "other_error",
    };
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
        try { requests.Add(1, tags); duration.Record(seconds, tags); }
        catch { /* Operator listener failure cannot change an authoritative result. */ }
    }
}
public sealed class OrganizationTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, OrganizationTelemetry telemetry)
    {
        var start = Stopwatch.GetTimestamp(); var threw = false;
        try { await next(context); } catch { threw = true; throw; }
        finally
        {
            var operation = OrganizationTelemetry.Operation(context);
            if (operation is not null) telemetry.Record(context, operation, Stopwatch.GetElapsedTime(start).TotalSeconds, threw);
        }
    }
}
public sealed class OrganizationTelemetryResultFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (result is IValueHttpResult value && value.Value is ProblemDetails problem && problem.Extensions.TryGetValue("code", out var code))
            OrganizationTelemetry.SetError(context.HttpContext, code as string);
        return result;
    }
}
