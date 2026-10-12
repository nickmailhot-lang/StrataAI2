using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;

namespace StrataAI.Api.Records;

// Deliberate transport projection: no protected titles, bodies, file URLs or
// client-supplied actor/membership/capacity. Evidence is never a permission grant.
public sealed record RetentionSourceReviewRequest(
    [property: JsonRequired] Guid OrganizationId,
    [property: JsonRequired] Guid RecordId,
    [property: JsonRequired] RetentionProducerKind Kind,
    [property: JsonRequired] Guid? ParentCardId,
    [property: JsonRequired] Guid CardId,
    [property: JsonRequired] Guid BoardId,
    [property: JsonRequired] Guid ListId,
    [property: JsonRequired] long CardVersion,
    [property: JsonRequired] long ListVersion,
    [property: JsonRequired] long SourceVersion,
    [property: JsonRequired] WorkItemLifecycleState CardState,
    [property: JsonRequired] WorkItemLifecycleState ListState,
    [property: JsonRequired] WorkItemLifecycleState SourceState,
    [property: JsonRequired] DateTimeOffset CreatedAt)
{
    public RetentionProducerReference Evidence() => new(OrganizationId, RecordId, Kind, ParentCardId,
        CardId, BoardId, ListId, CardVersion, ListVersion, SourceVersion, CardState, ListState, SourceState, CreatedAt);
    public static RetentionSourceReviewRequest From(RetentionProducerReference source) => new(source.OrganizationId,
        source.RecordId, source.Kind, source.ParentCardId, source.CardId, source.BoardId, source.ListId,
        source.CardVersion, source.ListVersion, source.SourceVersion, source.CardState, source.ListState, source.SourceState, source.CreatedAt);
}
public sealed record ClassifyRetentionSourceRequest(
    [property: JsonRequired] RetentionSourceReviewRequest? Review,
    [property: JsonRequired] string Category,
    [property: JsonRequired] string? Jurisdiction,
    [property: JsonRequired] bool Confirmed);

// Both provider compositions own the same review/confirmation/recovery boundary.
public static class RetentionSourceEndpoints
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter<RetentionProducerKind>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<WorkItemLifecycleState>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<RetentionRecordClass>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
    public static void MapRetentionSourceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{organizationId:guid}/retention-sources/{kind}/{recordId:guid}/review", ReviewAsync);
        group.MapGet("/{organizationId:guid}/retention-holds/sources/{kind}/{recordId:guid}/review", ReviewHoldAsync);
        group.MapPost("/{organizationId:guid}/retention-sources/{kind}/{recordId:guid}/classification", ClassifyAsync);
    }
    public static async Task<IResult> ReviewHoldAsync(Guid organizationId, string kind, Guid recordId,
        HttpContext context, RetentionHoldReviewService service, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        // Unsupported route kinds remain a safe not-found after owning admission.
        Kind(kind, out var producer);
        var result = await service.ReviewAsync(organizationId, actor.Value, producer, recordId, ct);
        return result.Succeeded ? Results.Json(RetentionSourceReviewRequest.From(result.Value!), Options) : HoldReviewError(result.ErrorCode);
    }
    private static IResult HoldReviewError(string? code)
    {
        var status = code == "session_unavailable" ? 401 : code == "organization_storage_unavailable" ? 503 : 404;
        return Results.Problem(statusCode: status, title: status == 401 ? "Sign in again to continue."
            : status == 503 ? "The hold source review could not be confirmed. Retry shortly." : "The hold source was not found.",
            extensions: new Dictionary<string, object?> { ["code"] = status == 404 ? "retention_hold_not_found" : code });
    }
    public static async Task<IResult> ReviewAsync(Guid organizationId, string kind, Guid recordId,
        HttpContext context, RetentionSourceReviewService service, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        var admission = await service.AdmitAsync(organizationId, actor.Value, ct);
        if (!admission.Succeeded) return Error(admission.ErrorCode);
        if (!Kind(kind, out var producer)) return Error("retention_source_unavailable");
        var result = await service.ReviewAsync(organizationId, actor.Value, producer, recordId, ct);
        return result.Succeeded ? Results.Json(RetentionSourceReviewRequest.From(result.Value!), Options) : Error(result.ErrorCode);
    }
    public static async Task<IResult> ClassifyAsync(Guid organizationId, string kind, Guid recordId,
        HttpContext context, RetentionSourceReviewService reviews, RetentionSourceClassificationService commands, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var actor = Actor(context); if (actor is null) return Error("session_unavailable");
        var admission = await reviews.AdmitAsync(organizationId, actor.Value, ct);
        if (!admission.Succeeded) return Error(admission.ErrorCode);
        if (!Kind(kind, out var producer)) return Error("retention_source_unavailable");
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0]?.Length != 36 || !Guid.TryParseExact(keys[0], "D", out var key) || key == Guid.Empty)
            return Error("idempotency_key_required");
        if (!context.Request.HasJsonContentType()) return Error("invalid_retention_source_request");
        ClassifyRetentionSourceRequest? request;
        try
        {
            using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, ct)) != 0)
            {
                if (body.Length + count > 32_768) return Error("invalid_retention_source_request");
                body.Write(buffer, 0, count);
            }
            request = JsonSerializer.Deserialize<ClassifyRetentionSourceRequest>(body.ToArray(), Options);
        }
        catch (JsonException) { return Error("invalid_retention_source_request"); }
        if (request is not { Confirmed: true, Review: { } review }
            || review.OrganizationId != organizationId || review.RecordId != recordId || review.Kind != producer)
            return Error("invalid_retention_source_request");
        // No fresh review here: exact original evidence must remain recoverable
        // after a move. The owning command authorizes original AND current Boards.
        var result = await commands.ClassifyAsync(organizationId, actor.Value, review.Evidence(), request.Category,
            request.Jurisdiction, key, context.TraceIdentifier, ct);
        return result.Succeeded ? Results.Json(result.Value, Options) : Error(result.ErrorCode);
    }
    private static bool Kind(string value, out RetentionProducerKind kind)
    {
        kind = value switch { "card" => RetentionProducerKind.Card, "cardComment" => RetentionProducerKind.CardComment,
            "attachment" => RetentionProducerKind.Attachment, _ => RetentionProducerKind.AuditEvent };
        return kind != RetentionProducerKind.AuditEvent;
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
            "organization_storage_unavailable" => (503, "The classification could not be confirmed. Retry shortly."),
            "idempotency_key_conflict" => (409, "This retry key belongs to a different classification."),
            "idempotency_key_expired" => (409, "The original acknowledgment expired."),
            "idempotency_key_required" => (400, "A nonempty UUID retry key is required."),
            "invalid_retention_source_request" => (400, "Review the source evidence and explicitly confirm the classification."),
            "invalid_retention_classification" => (400, "Provide a supported category and jurisdiction."),
            _ => (404, "The source was not found.")
        };
        return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?>
            { ["code"] = status == 404 ? "retention_source_unavailable" : code });
    }
}
