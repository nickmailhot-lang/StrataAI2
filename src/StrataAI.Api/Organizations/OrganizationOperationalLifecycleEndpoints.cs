using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrataAI.Application.Organizations;

namespace StrataAI.Api.Organizations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeOrganizationOperationalLifecycleRequest(
    [property: JsonRequired] long Version, [property: JsonRequired] string? Status);

public static class OrganizationOperationalLifecycleEndpoints
{
    private static readonly JsonSerializerOptions InputOptions = new(JsonSerializerDefaults.Web) { AllowDuplicateProperties = false };
    public static void MapOrganizationOperationalLifecycleEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{organizationId:guid}/operational-lifecycle/events", async (Guid organizationId, HttpContext context,
            OrganizationOperationalLifecycleReplayService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            long after = 0;
            if (context.Request.Query.TryGetValue("afterVersion", out var values)
                && (values.Count != 1 || !long.TryParse(values[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out after))) return Error("invalid_organization_cursor");
            var result = await service.ReadAsync(organizationId, actor.Value, after, ct);
            if (!result.Succeeded) return Error(result.ErrorCode);
            var page = result.Value!;
            return Results.Ok(new { page.OrganizationId, page.ActorId, Status = State(page.Status), page.Version,
                page.AfterVersion, page.NextVersion, page.Pending, Events = page.Events.Select(source => new {
                    source.EventId, source.OrganizationId, source.ActorId, source.EventType, source.EntityType, source.EntityId,
                    source.Version, PreviousStatus = State(source.PreviousStatus), Status = State(source.Status),
                    source.ActingCapacity, source.CreatedAt }) });
        });
        group.MapGet("/operational-lifecycle-directory", async (HttpContext context,
            OrganizationOperationalLifecycleDirectoryService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            Guid? after = null;
            if (context.Request.Query.TryGetValue("after", out var values))
            {
                if (values.Count != 1 || values[0]?.Length != 36 || !Guid.TryParseExact(values[0], "D", out var parsed) || parsed == Guid.Empty)
                    return Error("invalid_organization_cursor");
                after = parsed;
            }
            var result = await service.ReadAsync(actor.Value, after, ct);
            if (!result.Succeeded) return Error(result.ErrorCode);
            var page = result.Value!;
            return Results.Ok(new { page.ActorId, page.NextCursor, Items = page.Items.Select(review => new {
                review.OrganizationId, review.ActorId, review.Name, Status = State(review.Status),
                review.Version, review.UpdatedAt, review.ActingCapacity }) });
        });
        group.MapGet("/{organizationId:guid}/operational-lifecycle", async (Guid organizationId,
            HttpContext context, OrganizationOperationalLifecycleService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var result = await service.ReviewAsync(organizationId, actor.Value, ct);
            if (!result.Succeeded) return Error(result.ErrorCode);
            var review = result.Value!;
            return Results.Ok(new { review.OrganizationId, review.ActorId, Status = State(review.Status),
                review.Version, review.UpdatedAt, review.ActingCapacity, review.Name });
        });
        group.MapPatch("/{organizationId:guid}/operational-lifecycle", async (Guid organizationId,
            HttpContext context, OrganizationOperationalLifecycleService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            // Current retained-parent administrative admission precedes parsing
            // guessed targets, retry keys or private submitted content.
            var admission = await service.ReviewAsync(organizationId, actor.Value, ct);
            if (!admission.Succeeded) return Error(admission.ErrorCode);
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
                || keys[0]?.Length != 36 || !Guid.TryParseExact(keys[0], "D", out var key) || key == Guid.Empty)
                return Error("idempotency_key_required");
            if (!context.Request.HasJsonContentType()) return Error("invalid_organization_lifecycle_request");
            ChangeOrganizationOperationalLifecycleRequest? request;
            try
            {
                using var body = new MemoryStream(); var buffer = new byte[1024]; int count;
                while ((count = await context.Request.Body.ReadAsync(buffer, ct)) != 0)
                {
                    if (body.Length + count > 4096) return Error("invalid_organization_lifecycle_request");
                    body.Write(buffer, 0, count);
                }
                request = JsonSerializer.Deserialize<ChangeOrganizationOperationalLifecycleRequest>(body.ToArray(), InputOptions);
            }
            catch (JsonException) { return Error("invalid_organization_lifecycle_request"); }
            if (request is null) return Error("invalid_organization_lifecycle_request");
            var status = request.Status switch
            {
                "ACTIVE" => OrganizationStatus.Active, "SUSPENDED" => OrganizationStatus.Suspended,
                "DEACTIVATED" => OrganizationStatus.Deactivated, _ => (OrganizationStatus)(-1),
            };
            var result = await service.ChangeAsync(organizationId, actor.Value, status, request.Version,
                key, context.TraceIdentifier, ct);
            if (!result.Succeeded) return Error(result.ErrorCode);
            var source = result.Value!;
            return Results.Ok(new { source.OrganizationId, source.EventId, source.ActorId, source.ActorLabel,
                source.ActingCapacity, PreviousStatus = State(source.PreviousStatus), Status = State(source.Status),
                source.Version, source.CorrelationId, source.CreatedAt });
        });
    }
    private static string State(OrganizationStatus status) => status switch
    {
        OrganizationStatus.Active => "ACTIVE", OrganizationStatus.Suspended => "SUSPENDED",
        OrganizationStatus.Deactivated => "DEACTIVATED", _ => throw new InvalidOperationException("Invalid lifecycle source."),
    };
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
            "session_unavailable" => (401, "Your session is no longer available. Sign in again."),
            "organization_storage_unavailable" or "organization_lifecycle_source_unavailable" => (503, "The lifecycle could not be confirmed. Retry shortly."),
            "version_conflict" => (409, "Organization changed elsewhere. Review the current revision."),
            "idempotency_key_conflict" => (409, "The retry key belongs to a different lifecycle change."),
            "idempotency_key_expired" => (409, "The original acknowledgment expired. Review the current lifecycle."),
            "organization_lifecycle_unchanged" => (409, "Organization already has the requested lifecycle state."),
            "idempotency_key_required" => (400, "A nonempty UUID retry key is required."),
            "invalid_organization_cursor" => (400, "Use a valid continuation returned by this lifecycle endpoint."),
            "invalid_organization_lifecycle" => (400, "Choose ACTIVE, SUSPENDED or DEACTIVATED."),
            "invalid_organization_lifecycle_request" or "invalid_correlation_id" => (400, "Supply one lifecycle state and reviewed revision."),
            _ => (404, "Organization not found."),
        };
        return Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = status == 404 ? "organization_not_found" : code });
    }
}
