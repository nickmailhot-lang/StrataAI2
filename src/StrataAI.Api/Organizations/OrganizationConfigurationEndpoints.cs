using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;

namespace StrataAI.Api.Organizations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeOrganizationConfigurationRequest(
    [property: JsonRequired] long Version,
    [property: JsonRequired] OrganizationConfigurationData? Configuration);

public static class OrganizationConfigurationEndpoints
{
    private static readonly JsonSerializerOptions InputOptions = new(JsonSerializerDefaults.Web) { AllowDuplicateProperties = false };
    private const int MaximumRequestBytes = 98_304;

    public static void MapOrganizationConfigurationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{organizationId:guid}/configuration", async (Guid organizationId,
            HttpContext context, OrganizationConfigurationService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var result = await service.ReadAsync(organizationId, actor.Value, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });

        group.MapGet("/{organizationId:guid}/configuration/history", async (Guid organizationId,
            HttpContext context, OrganizationConfigurationService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            // Check current private-record authority before validating a guessed target's cursor.
            var admission = await service.ReadAsync(organizationId, actor.Value, ct);
            if (!admission.Succeeded) return Error(admission.ErrorCode);
            long? before = null;
            if (context.Request.Query.TryGetValue("beforeVersion", out var values))
            {
                if (values.Count != 1 || !long.TryParse(values[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    return Error("invalid_configuration_cursor");
                before = parsed;
            }
            var result = await service.ReadHistoryAsync(organizationId, actor.Value, before, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });

        group.MapPatch("/{organizationId:guid}/configuration", async (Guid organizationId,
            HttpContext context, OrganizationConfigurationService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var admission = await service.ReadAsync(organizationId, actor.Value, ct);
            if (!admission.Succeeded) return Error(admission.ErrorCode);
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
                || keys[0]?.Length != 36 || !Guid.TryParseExact(keys[0], "D", out var key) || key == Guid.Empty)
                return Error("idempotency_key_required");
            if (!context.Request.HasJsonContentType()) return Error("invalid_configuration_request");
            ChangeOrganizationConfigurationRequest? request;
            try
            {
                // Bound chunked as well as fixed-length requests; never log rejected private values.
                using var body = new MemoryStream(); var buffer = new byte[8192];
                int count;
                while ((count = await context.Request.Body.ReadAsync(buffer, ct)) != 0)
                {
                    if (body.Length + count > MaximumRequestBytes) return Error("invalid_configuration_request");
                    body.Write(buffer, 0, count);
                }
                request = JsonSerializer.Deserialize<ChangeOrganizationConfigurationRequest>(body.ToArray(), InputOptions);
            }
            catch (JsonException) { return Error("invalid_configuration_request"); }
            if (request is null) return Error("invalid_configuration_request");
            var result = await service.ChangeAsync(organizationId, actor.Value, request.Configuration,
                request.Version, key, context.TraceIdentifier, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
    }

    private static Guid? Actor(HttpContext context)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty)
            return null;
        if (context.Request.Headers.TryGetValue("X-StrataAI-Expected-Actor", out var values)
            && (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var expected) || expected != actor))
            return null;
        return actor;
    }

    private static IResult Error(string? code)
    {
        var (status, title) = code switch
        {
            "session_unavailable" => (401, "Your session is no longer available. Sign in again."),
            "organization_storage_unavailable" or "configuration_source_unavailable" => (503, "The configuration could not be confirmed. Retry shortly."),
            "version_conflict" => (409, "Configuration changed elsewhere. Review the current revision."),
            "idempotency_key_conflict" => (409, "The retry key belongs to a different configuration change."),
            "idempotency_key_expired" => (409, "The original acknowledgment expired. Review the current configuration."),
            "configuration_identifier_unavailable" => (409, "The registration identifier is unavailable for this jurisdiction."),
            "configuration_intake_unavailable" => (400, "Choose an active intake Board and a List belonging to it."),
            "idempotency_key_required" => (400, "A nonempty UUID retry key is required."),
            "invalid_configuration_cursor" => (400, "A positive configuration history revision is required."),
            "invalid_configuration_request" => (400, "Provide a configuration and its reviewed revision using the supported fields."),
            { } value when value.StartsWith("invalid_configuration_", StringComparison.Ordinal) => (400, "Correct the indicated configuration field."),
            _ => (404, "The Organization was not found."),
        };
        return Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = status == 404 ? "organization_not_found" : code });
    }
}
