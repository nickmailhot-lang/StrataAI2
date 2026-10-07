using System.Security.Claims;
using StrataAI.Application.Organizations;

namespace StrataAI.Api.Organizations;

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/organizations").RequireAuthorization();
        if (app.Services.GetRequiredService<StrataAI.Application.Runtime.RuntimeDescriptor>().Mode == StrataAI.Application.Runtime.RuntimeMode.Production)
        {
            group.MapGet("/{organizationId:guid}/lifecycle-events", async (Guid organizationId, Guid? expectedActorId,
                HttpContext context, IOrganizationLifecycleEventReader reader, CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
                if (expectedActorId is Guid expected && expected != actor.Value) return ErrorFor("session_unavailable");
                var result = await reader.ReadAsync(organizationId, actor.Value, cancellationToken);
                return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
            });
            group.MapGet("/{organizationId:guid}/metadata-events", async (Guid organizationId, Guid? expectedActorId,
                string? cursor, int? limit, HttpContext context, TransactionalOrganizationMetadataSynchronization replay,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
                if (expectedActorId is Guid expected && expected != actor.Value) return ErrorFor("session_unavailable");
                var result = await replay.ReadAsync(organizationId, actor.Value, cursor, limit ?? 50, cancellationToken);
                if (result.Succeeded) return Results.Ok(result.Value);
                return result.ErrorCode switch
                {
                    "invalid_sync_limit" => Problem(400, "invalid_sync_limit", "A replay page limit from 1 to 100 is required."),
                    "organization_sync_unavailable" or "work_storage_unavailable" => Problem(503, "organization_sync_unavailable", "Organization events are temporarily unavailable."),
                    _ => ErrorFor(result.ErrorCode),
                };
            });
        }

        group.MapGet("/{organizationId:guid}/deletion-requests/{requestId:guid}", async (Guid organizationId,
            Guid requestId, Guid? expectedActorId, HttpContext context,
            IOrganizationDeletionObservationReader reader, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            if (expectedActorId is Guid expected && expected != actor.Value)
                return ErrorFor("session_unavailable");
            var result = await reader.ReadAsync(organizationId, actor.Value, requestId, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        group.MapGet("/directory", async (string? after, HttpContext context,
            IOrganizationService service, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            Guid? cursor = null;
            if (after is not null)
            {
                if (after.Length != 36 || !Guid.TryParseExact(after, "D", out var parsed) || parsed == Guid.Empty)
                    return ErrorFor("invalid_organization_cursor");
                cursor = parsed;
            }
            var result = await service.ListPageAsync(actor.Value, cursor, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        group.MapGet("/{organizationId:guid}/boards/directory", async (Guid organizationId, string? after,
            HttpContext context, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            Guid? cursor = null;
            if (after is not null)
            {
                if (after.Length != 36 || !Guid.TryParseExact(after, "D", out var parsed) || parsed == Guid.Empty)
                    return ErrorFor("invalid_board_directory_cursor");
                cursor = parsed;
            }
            var result = await service.ListBoardsPageAsync(organizationId, actor.Value, cursor, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        group.MapGet("/{organizationId:guid}", async (Guid organizationId, HttpContext context,
            IOrganizationService service, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            if (!ReviewedActorMatches(context, actor.Value)) return ErrorFor("session_unavailable");
            var result = await service.ReadAsync(organizationId, actor.Value, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        group.MapGet("/{organizationId:guid}/surface-access", async (Guid organizationId, string? surface,
            HttpContext context, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            if (surface is not ("INTERNAL" or "PORTAL"))
                return Problem(StatusCodes.Status400BadRequest, "invalid_access_surface", "Choose a valid access surface.");
            var result = await service.ReadSurfaceAdmissionAsync(organizationId, actor.Value, surface == "PORTAL", cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        group.MapGet("/{organizationId:guid}/members/{targetUserId:guid}", async (Guid organizationId,
            Guid targetUserId, HttpContext context, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            var result = await service.ReviewMemberAsync(organizationId, actor.Value, targetUserId, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        group.MapGet("/{organizationId:guid}/members", async (Guid organizationId, string? after,
            HttpContext context, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId is null) return Results.Unauthorized();
            Guid? cursor = null;
            if (after is not null)
            {
                if (after.Length != 36 || !Guid.TryParseExact(after, "D", out var parsed) || parsed == Guid.Empty)
                    return ErrorFor("invalid_member_cursor");
                cursor = parsed;
            }
            var result = await service.ListMembersAsync(organizationId, userId.Value, cursor, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        group.MapPost(
            "/",
            async (
                CreateOrganizationRequest request,
                Guid? expectedActorId,
                HttpContext context,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                context.Response.Headers.CacheControl = "private, no-store";
                if (expectedActorId is Guid expected && expected != userId.Value)
                    return ErrorFor("session_unavailable");
                Guid? idempotencyKey = null;
                if (context.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
                {
                    if (keys.Count != 1 || keys[0]?.Length != 36 ||
                        !Guid.TryParseExact(keys[0], "D", out var key) || key == Guid.Empty)
                        return ErrorFor("invalid_idempotency_key");
                    idempotencyKey = key;
                }
                var result = await service.CreateAsync(
                    userId.Value,
                    request.Name,
                    request.Description,
                    context.TraceIdentifier,
                    cancellationToken, idempotencyKey);

                if (!result.Succeeded || result.Value is null)
                {
                    return ErrorFor(result.ErrorCode);
                }

                return Results.Created(
                    $"/organizations/{result.Value.Organization.Id}",
                    result.Value);
            });

        group.MapGet(
            "/",
            async (
                HttpContext context,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(
                    await service.ListAsync(userId.Value, cancellationToken));
            });

        group.MapPatch(
            "/{organizationId:guid}",
            async (
                Guid organizationId,
                UpdateOrganizationRequest request,
                HttpContext context,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                context.Response.Headers.CacheControl = "private, no-store";
                if (!ReviewedActorMatches(context, userId.Value)) return ErrorFor("session_unavailable");
                Guid? key = null;
                if (context.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
                {
                    if (keys.Count != 1 || !Guid.TryParse(keys[0], out var parsed) || parsed == Guid.Empty)
                        return ErrorFor("invalid_idempotency_key");
                    key = parsed;
                }
                var result = await service.UpdateAsync(
                    organizationId,
                    userId.Value,
                    request.Name,
                    request.Description,
                    request.LogoUrl,
                    request.Version,
                    context.TraceIdentifier,
                    cancellationToken, key);

                return result.Succeeded && result.Value is not null
                    ? Results.Ok(result.Value)
                    : ErrorFor(result.ErrorCode);
            });

        group.MapGet(
            "/{organizationId:guid}/boards",
            async (
                Guid organizationId,
                HttpContext context,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.ListBoardsAsync(
                    organizationId,
                    userId.Value,
                    cancellationToken);

                return result.Succeeded && result.Value is not null
                    ? Results.Ok(result.Value)
                    : ErrorFor(result.ErrorCode);
            });

        group.MapDelete(
            "/{organizationId:guid}/members/{targetUserId:guid}",
            async (
                Guid organizationId,
                Guid targetUserId,
                long? expectedVersion,
                Guid? expectedActorId,
                HttpContext context,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                if (expectedActorId is Guid expected && expected != userId.Value)
                    return ErrorFor("session_unavailable");
                Guid? key = null;
                if (context.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
                {
                    if (keys.Count != 1 || !Guid.TryParse(keys[0], out var parsed) || parsed == Guid.Empty)
                        return ErrorFor("invalid_idempotency_key");
                    key = parsed;
                }
                var result = await service.RemoveMemberAsync(
                    organizationId,
                    userId.Value,
                    targetUserId,
                    context.TraceIdentifier,
                    cancellationToken,
                    expectedVersion, key);

                return result.Succeeded
                    ? Results.NoContent()
                    : ErrorFor(result.ErrorCode);
            });

        group.MapPost(
            "/{organizationId:guid}/leave",
            async (
                Guid organizationId,
                HttpContext context,
                IOrganizationService service,
                CancellationToken cancellationToken, LeaveOrganizationRequest? request = null) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                if (request?.ExpectedActorId is Guid expected && expected != userId.Value)
                    return ErrorFor("session_unavailable");

                Guid? key = null;
                if (context.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
                {
                    if (keys.Count != 1 || !Guid.TryParse(keys[0], out var parsed) || parsed == Guid.Empty)
                        return ErrorFor("invalid_idempotency_key");
                    key = parsed;
                }
                var result = await service.LeaveAsync(
                    organizationId,
                    userId.Value,
                    context.TraceIdentifier,
                    cancellationToken, key);

                return result.Succeeded
                    ? Results.NoContent()
                    : ErrorFor(result.ErrorCode);
            });

        group.MapDelete(
            "/{organizationId:guid}",
            async (
                Guid organizationId,
                long version,
                Guid? expectedActorId,
                HttpContext context,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                context.Response.Headers.CacheControl = "private, no-store";
                if (expectedActorId is Guid expected && expected != userId.Value)
                    return ErrorFor("session_unavailable");
                Guid? key = null;
                if (context.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
                {
                    if (keys.Count != 1 || keys[0]?.Length != 36 || !Guid.TryParseExact(keys[0], "D", out var parsed) || parsed == Guid.Empty)
                        return ErrorFor("invalid_idempotency_key");
                    key = parsed;
                }
                // A caller without a retry key receives a fresh request reference.
                // The browser supplies its own key so lost responses remain recoverable.
                key ??= Guid.NewGuid();
                var result = await service.MarkDeletingAsync(
                    organizationId,
                    userId.Value,
                    version,
                    context.TraceIdentifier,
                    cancellationToken, key);

                return result.Succeeded
                    ? Results.Accepted($"/organizations/{organizationId:D}/deletion-requests/{key.Value:D}", new { requestId = key.Value })
                    : ErrorFor(result.ErrorCode);
            });
    }

    // Existing API clients may omit this header. Settings reviews and commands
    // bind their immutable intent to the account whose private data was reviewed.
    private static bool ReviewedActorMatches(HttpContext context, Guid actor) =>
        !context.Request.Headers.TryGetValue("X-StrataAI-Expected-Actor", out var expected) ||
        expected.Count == 1 && Guid.TryParse(expected[0], out var id) && id != Guid.Empty && id == actor;

    private static Guid? GetUserId(HttpContext context)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static IResult ErrorFor(string? errorCode) =>
        errorCode switch
        {
            "invalid_idempotency_key" => Problem(StatusCodes.Status400BadRequest, errorCode, "A nonempty UUID retry key is required."),
            "idempotency_conflict" => Problem(StatusCodes.Status409Conflict, errorCode, "The retry key belongs to a different Organization change."),
            "idempotency_expired" => Problem(StatusCodes.Status409Conflict, errorCode, "The original acknowledgment has expired. Review current settings."),
            "invalid_member_version" => Problem(StatusCodes.Status400BadRequest, errorCode,
                "A positive membership version is required."),
            "member_version_conflict" => Problem(StatusCodes.Status409Conflict, errorCode,
                "The membership changed elsewhere. Review the current membership before removing it."),
            "invalid_board_directory_cursor" => Problem(StatusCodes.Status400BadRequest, errorCode,
                "The Board directory page cursor is invalid."),
            "invalid_organization_cursor" => Problem(StatusCodes.Status400BadRequest, errorCode,
                "The Organization page cursor is invalid."),
            "invalid_member_cursor" => Problem(StatusCodes.Status400BadRequest, errorCode,
                "The member page cursor is invalid."),
            "session_unavailable" => Problem(
                StatusCodes.Status401Unauthorized,
                errorCode,
                "Your session is no longer available. Sign in again."),
            "organization_storage_unavailable" => Problem(
                StatusCodes.Status503ServiceUnavailable,
                errorCode,
                "The Organization change could not be confirmed. Retry shortly."),
            "invalid_organization_name" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid Organization name is required."),
            "invalid_organization_logo_url" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A secure HTTPS logo URL without embedded credentials is required."),
            "version_conflict" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "The Organization changed elsewhere. Refresh and retry."),
            "sole_owner" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "The sole active owner cannot leave or be removed."),
            "insufficient_permission" => Problem(
                StatusCodes.Status403Forbidden,
                errorCode,
                "The action is not permitted."),
            "member_not_found" => Problem(
                StatusCodes.Status404NotFound,
                errorCode,
                "The member was not found."),
            _ => Problem(
                StatusCodes.Status404NotFound,
                "organization_not_found",
                "The Organization was not found."),
        };

    private static IResult Problem(
        int status,
        string code,
        string title) =>
        Results.Problem(
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            });
}
