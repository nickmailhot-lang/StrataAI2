using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.Records;

public sealed record ApplyRetentionHoldRequest(
    [property: JsonRequired] RetentionRecordClass? RecordClass,
    [property: JsonRequired] string? Category,
    [property: JsonRequired] Guid? RecordId,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] RetentionSourceReviewRequest? Review,
    [property: JsonRequired] bool Confirmed);
public sealed record ReleaseRetentionHoldRequest([property: JsonRequired] long Version,
    [property: JsonRequired] bool Confirmed);

public static class RetentionHoldEndpoints
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter<RetentionRecordClass>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<RetentionHoldStatus>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<RetentionProducerKind>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<WorkItemLifecycleState>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
    public static void MapRetentionHoldEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{organizationId:guid}/retention-holds", DirectoryAsync);
        group.MapGet("/{organizationId:guid}/retention-holds/{holdId:guid}", ReadAsync);
        group.MapGet("/{organizationId:guid}/retention-holds/{holdId:guid}/history", HistoryAsync);
        group.MapPost("/{organizationId:guid}/retention-holds/{holdId:guid}/apply", ApplyAsync);
        group.MapPost("/{organizationId:guid}/retention-holds/{holdId:guid}/release", ReleaseAsync);
    }
    public static async Task<IResult> DirectoryAsync(Guid organizationId, HttpContext context,
        RetentionSourceReviewService admission, RetentionHoldService commands, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        var admitted = await admission.AdmitAsync(organizationId, actor.Value, ct);
        if (!admitted.Succeeded) return Error(admitted.ErrorCode);
        Guid? after = null;
        if (context.Request.Query.TryGetValue("after", out var values))
        {
            if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var parsed) || parsed == Guid.Empty)
                return Error("invalid_retention_hold_cursor");
            after = parsed;
        }
        var result = await commands.ReadDirectoryAsync(organizationId, actor.Value, after, ct);
        return result.Succeeded ? Results.Json(result.Value, Options) : Error(result.ErrorCode);
    }
    public static async Task<IResult> ReadAsync(Guid organizationId, Guid holdId, HttpContext context,
        RetentionHoldService commands, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        var result = await commands.ReadAsync(organizationId, actor.Value, holdId, ct);
        return result.Succeeded ? Results.Json(result.Value, Options) : Error(result.ErrorCode);
    }
    public static async Task<IResult> HistoryAsync(Guid organizationId, Guid holdId, HttpContext context,
        RetentionHoldService commands, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        var result = await commands.ReadTimelineAsync(organizationId, actor.Value, holdId, ct);
        return result.Succeeded ? Results.Json(result.Value, Options) : Error(result.ErrorCode);
    }
    public static async Task<IResult> ApplyAsync(Guid organizationId, Guid holdId, HttpContext context,
        RetentionSourceReviewService admission, RetentionHoldService commands, RetentionProducerAuthorization reviewer, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        var admitted = await admission.AdmitAsync(organizationId, actor.Value, ct);
        if (!admitted.Succeeded) return Error(admitted.ErrorCode);
        if (!Key(context, out var key)) return Error("idempotency_key_required");
        var request = await BodyAsync<ApplyRetentionHoldRequest>(context, ct);
        if (request is not { Confirmed: true }) return Error("invalid_retention_hold_request");
        var scope = new RetentionHoldScope(organizationId, request.RecordClass, request.Category, request.RecordId);
        var result = await commands.ApplyReviewedAsync(organizationId, actor.Value, holdId, scope, request.Reason,
            request.Review?.Evidence(), reviewer, key, context.TraceIdentifier, ct);
        return result.Succeeded ? Results.Json(result.Value, Options) : Error(result.ErrorCode);
    }
    public static async Task<IResult> ReleaseAsync(Guid organizationId, Guid holdId, HttpContext context,
        RetentionSourceReviewService admission, RetentionHoldService commands, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        var admitted = await admission.AdmitAsync(organizationId, actor.Value, ct);
        if (!admitted.Succeeded) return Error(admitted.ErrorCode);
        if (!Key(context, out var key)) return Error("idempotency_key_required");
        var request = await BodyAsync<ReleaseRetentionHoldRequest>(context, ct);
        if (request is not { Confirmed: true }) return Error("invalid_retention_hold_request");
        var result = await commands.ReleaseAsync(organizationId, actor.Value, holdId, request.Version, key, context.TraceIdentifier, ct);
        return result.Succeeded ? Results.Json(result.Value, Options) : Error(result.ErrorCode);
    }
    private static async Task<T?> BodyAsync<T>(HttpContext context, CancellationToken ct) where T : class
    {
        if (!context.Request.HasJsonContentType()) return null;
        try
        {
            using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, ct)) != 0)
            {
                if (body.Length + count > 32_768) return null;
                body.Write(buffer, 0, count);
            }
            return JsonSerializer.Deserialize<T>(body.ToArray(), Options);
        }
        catch (JsonException) { return null; }
    }
    private static bool Key(HttpContext context, out Guid key)
    {
        key = Guid.Empty;
        return context.Request.Headers.TryGetValue("Idempotency-Key", out var values) && values.Count == 1
            && values[0]?.Length == 36 && Guid.TryParseExact(values[0], "D", out key) && key != Guid.Empty;
    }
    private static Guid? Actor(HttpContext context)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty) return null;
        if (context.Request.Headers.TryGetValue("X-StrataAI-Expected-Actor", out var values)
            && (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var expected) || expected != actor)) return null;
        return actor;
    }
    private static IResult Error(string? code)
    {
        var (status, title) = code switch
        {
            "session_unavailable" => (401, "Sign in again to continue."),
            "organization_storage_unavailable" or "retention_hold_source_unavailable" => (503, "The hold could not be confirmed. Retry the original request."),
            "version_conflict" => (409, "The hold changed. Review its current version."),
            "retention_hold_review_changed" => (409, "The source changed. Review it again."),
            "retention_hold_already_released" => (409, "The hold has already been released."),
            "idempotency_key_conflict" => (409, "This retry key belongs to a different request."),
            "idempotency_key_expired" => (409, "The original acknowledgment expired."),
            "idempotency_key_required" => (400, "A nonempty UUID retry key is required."),
            "invalid_retention_hold_cursor" or "invalid_retention_hold_request" or "invalid_retention_hold" or "invalid_retention_hold_review" => (400, "Review the hold scope, reason and source evidence, then explicitly confirm."),
            _ => (404, "The hold was not found.")
        };
        return Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = status == 404 ? "retention_hold_not_found" : code });
    }
}
