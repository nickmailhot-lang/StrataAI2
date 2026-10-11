using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;

namespace StrataAI.Api.Records;

public sealed record RetentionPolicyDataRequest(
    [property: JsonRequired] RetentionRecordClass RecordClass,
    [property: JsonRequired] string Category,
    string? Jurisdiction,
    [property: JsonRequired] RetentionAnchor Anchor,
    [property: JsonRequired] int? RetentionDays,
    [property: JsonRequired] RecordDispositionAction Action,
    [property: JsonRequired] string Source,
    string? Notes);
public sealed record ChangeRetentionPolicyRequest([property: JsonRequired] long Version,
    [property: JsonRequired] RetentionPolicyDataRequest? Policy);

public static class RetentionPolicyEndpoints
{
    private static readonly JsonSerializerOptions InputOptions = Options();
    private static JsonSerializerOptions Options()
    {
        var result = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        result.Converters.Add(new JsonStringEnumConverter<RetentionRecordClass>(JsonNamingPolicy.CamelCase, false));
        result.Converters.Add(new JsonStringEnumConverter<RetentionAnchor>(JsonNamingPolicy.CamelCase, false));
        result.Converters.Add(new JsonStringEnumConverter<RecordDispositionAction>(JsonNamingPolicy.CamelCase, false));
        return result;
    }

    public static void MapRetentionPolicyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{organizationId:guid}/retention-policies", async (Guid organizationId,
            HttpContext context, RetentionPolicyService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var admission = await service.AdmitAsync(organizationId, actor.Value, ct);
            if (!admission.Succeeded) return Error(admission.ErrorCode);
            Guid? after = null;
            if (context.Request.Query.TryGetValue("after", out var values))
            {
                if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var parsed) || parsed == Guid.Empty)
                    return Error("invalid_retention_policy_cursor");
                after = parsed;
            }
            var result = await service.ReadPoliciesAsync(organizationId, actor.Value, after, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
        group.MapGet("/{organizationId:guid}/retention-policies/{policyId:guid}", async (Guid organizationId, Guid policyId,
            HttpContext context, RetentionPolicyService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var result = await service.ReadAsync(organizationId, actor.Value, policyId, ct);
            return result.Succeeded ? result.Value is null ? Error("retention_policy_not_found") : Results.Ok(result.Value) : Error(result.ErrorCode);
        });
        group.MapGet("/{organizationId:guid}/retention-policies/{policyId:guid}/history", async (Guid organizationId, Guid policyId,
            HttpContext context, RetentionPolicyService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var admission = await service.AdmitAsync(organizationId, actor.Value, ct);
            if (!admission.Succeeded) return Error(admission.ErrorCode);
            long? before = null;
            if (context.Request.Query.TryGetValue("beforeVersion", out var values))
            {
                if (values.Count != 1 || !long.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    return Error("invalid_retention_policy_cursor");
                before = parsed;
            }
            var result = await service.ReadHistoryAsync(organizationId, actor.Value, policyId, before, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
        group.MapPatch("/{organizationId:guid}/retention-policies/{policyId:guid}", async (Guid organizationId, Guid policyId,
            HttpContext context, RetentionPolicyService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = Actor(context); if (actor is null) return Error("session_unavailable");
            var admission = await service.AdmitAsync(organizationId, actor.Value, ct);
            if (!admission.Succeeded) return Error(admission.ErrorCode);
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
                || keys[0]?.Length != 36 || !Guid.TryParseExact(keys[0], "D", out var key) || key == Guid.Empty)
                return Error("idempotency_key_required");
            if (!context.Request.HasJsonContentType()) return Error("invalid_retention_policy_request");
            ChangeRetentionPolicyRequest? request;
            try
            {
                using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
                while ((count = await context.Request.Body.ReadAsync(buffer, ct)) != 0)
                {
                    if (body.Length + count > 32_768) return Error("invalid_retention_policy_request");
                    body.Write(buffer, 0, count);
                }
                request = JsonSerializer.Deserialize<ChangeRetentionPolicyRequest>(body.ToArray(), InputOptions);
            }
            catch (JsonException) { return Error("invalid_retention_policy_request"); }
            if (request?.Policy is not { } data) return Error("invalid_retention_policy_request");
            var result = await service.ChangeAsync(organizationId, actor.Value, policyId, new(data.RecordClass, data.Category,
                data.Jurisdiction, data.Anchor, data.RetentionDays, data.Action, data.Source, data.Notes), request.Version,
                key, context.TraceIdentifier, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Error(result.ErrorCode);
        });
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
            "organization_storage_unavailable" or "retention_policy_source_unavailable" => (503, "The policy could not be confirmed. Retry shortly."),
            "version_conflict" => (409, "The policy changed elsewhere. Review its current revision."),
            "idempotency_key_conflict" => (409, "This retry key belongs to a different policy change."),
            "idempotency_key_expired" => (409, "The acknowledgment expired. Review the current policy."),
            "retention_policy_scope_unavailable" => (409, "A policy already reserves that applicability."),
            "retention_policy_scope_immutable" => (409, "Create a separately reviewed policy for different applicability."),
            "idempotency_key_required" => (400, "A nonempty UUID retry key is required."),
            "invalid_retention_policy_cursor" => (400, "Use the boundary returned by the previous page."),
            "invalid_retention_policy_request" => (400, "Provide the reviewed revision and supported policy fields."),
            { } value when value.StartsWith("invalid_retention_policy_", StringComparison.Ordinal) => (400, "Correct the indicated policy field."),
            "retention_policy_not_found" => (404, "The policy was not found."),
            _ => (404, "The Organization was not found.")
        };
        return Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = status == 404 && code != "retention_policy_not_found" ? "organization_not_found" : code });
    }
}
