using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrataAI.Application.Property;
using StrataAI.Domain.Property;

namespace StrataAI.Api.Organizations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StrataLotRequestData([property: JsonRequired] string UnitIdentifier,
    [property: JsonRequired] string LotIdentifier, [property: JsonRequired] string? CivicAddress,
    [property: JsonRequired] string? Building, [property: JsonRequired] string? Floor,
    [property: JsonRequired] string? Type, [property: JsonRequired] string Status);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeStrataLotRequest([property: JsonRequired] long Version, [property: JsonRequired] StrataLotRequestData? Data);

public static class StrataLotEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { AllowDuplicateProperties = false };
    public static void MapStrataLotEndpoints(this RouteGroupBuilder group) {
        group.MapGet("/{organizationId:guid}/strata-lots", async (Guid organizationId, HttpContext context,
            StrataLotService service, CancellationToken ct) => {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var admitted = await service.AdmitAsync(organizationId, actor.Value, ct);
            if (!admitted.Succeeded) return Error(admitted.ErrorCode);
            Guid? after = null;
            if (context.Request.Query.TryGetValue("after", out var values)) {
                if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var parsed) || parsed == Guid.Empty) return Error("invalid_strata_lot_cursor");
                after = parsed;
            }
            var result = await service.ListAsync(organizationId, actor.Value, after, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
        group.MapGet("/{organizationId:guid}/strata-lots/{lotId:guid}", async (Guid organizationId, Guid lotId,
            HttpContext context, StrataLotService service, CancellationToken ct) => {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var result = await service.ReadAsync(organizationId, actor.Value, lotId, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
        group.MapGet("/{organizationId:guid}/strata-lots/{lotId:guid}/history", async (Guid organizationId, Guid lotId,
            HttpContext context, StrataLotService service, CancellationToken ct) => {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var admitted = await service.AdmitAsync(organizationId, actor.Value, ct);
            if (!admitted.Succeeded) return Error(admitted.ErrorCode);
            long? before = null;
            if (context.Request.Query.TryGetValue("beforeVersion", out var values)) {
                if (values.Count != 1 || !long.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    return Error("invalid_strata_lot_cursor");
                before = parsed;
            }
            var result = await service.HistoryAsync(organizationId, actor.Value, lotId, before, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
        group.MapPatch("/{organizationId:guid}/strata-lots/{lotId:guid}", async (Guid organizationId, Guid lotId,
            HttpContext context, StrataLotService service, CancellationToken ct) => {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var admitted = await service.AdmitAsync(organizationId, actor.Value, ct);
            if (!admitted.Succeeded) return Error(admitted.ErrorCode);
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
                || !Guid.TryParseExact(keys[0], "D", out var key) || key == Guid.Empty) return Error("idempotency_key_required");
            if (context.Request.ContentLength is > 16384 || !context.Request.HasJsonContentType()) return Error("invalid_strata_lot_request");
            ChangeStrataLotRequest? request;
            try {
                using var body = new MemoryStream(); var buffer = new byte[4096]; int count;
                while ((count = await context.Request.Body.ReadAsync(buffer, ct)) != 0) {
                    if (body.Length + count > 16384) return Error("invalid_strata_lot_request");
                    body.Write(buffer, 0, count);
                }
                request = JsonSerializer.Deserialize<ChangeStrataLotRequest>(body.ToArray(), Json);
            }
            catch (JsonException) { return Error("invalid_strata_lot_request"); }
            if (request?.Data is not { } data) return Error("invalid_strata_lot_request");
            var result = await service.ChangeAsync(organizationId, actor.Value, lotId,
                new StrataLotData(data.UnitIdentifier, data.LotIdentifier, data.CivicAddress, data.Building, data.Floor, data.Type, data.Status),
                request.Version, key, context.TraceIdentifier, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
    }
    private static Guid? Actor(HttpContext context) {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty) return null;
        if (context.Request.Headers.TryGetValue("X-StrataAI-Expected-Actor", out var values)
            && (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var expected) || expected != actor)) return null;
        return actor;
    }
    private static IResult Error(string? code) {
        var status = code switch {
            "session_unavailable" => 401,
            "strata_lot_source_unavailable" or "organization_storage_unavailable" => 503,
            "version_conflict" or "idempotency_key_conflict" or "idempotency_key_expired" or "strata_lot_identifier_unavailable" => 409,
            "idempotency_key_required" => 400,
            { } value when value.StartsWith("invalid_strata_lot_", StringComparison.Ordinal) => 400,
            _ => 404
        };
        return Results.Problem(statusCode: status, title: status switch {
            401 => "Sign in again to continue.", 503 => "The unit directory could not be confirmed. Retry shortly.",
            409 => "Review the current unit and original change before continuing.", 400 => "Correct the indicated unit request field.",
            _ => "The requested record was not found."
        }, extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
