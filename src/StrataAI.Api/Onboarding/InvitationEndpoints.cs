using System.Security.Claims;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;

namespace StrataAI.Api.Onboarding;

public static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(
        this WebApplication app,
        RuntimeDescriptor runtime)
    {
        app.MapDelete("/boards/{boardId:guid}/invitations/{invitationId:guid}", async (Guid boardId,
            Guid invitationId, HttpContext context, BoardInvitationService service, CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            var result = await service.RevokeAsync(boardId, actor.Value, invitationId, context.TraceIdentifier, cancellationToken);
            return result.Succeeded ? Results.NoContent() : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().RequireRateLimiting("invitation");

        app.MapGet("/boards/{boardId:guid}/invitations", async (Guid boardId, string? after,
            HttpContext context, InvitationHistoryService service, CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            Guid? cursor = null;
            if (after is not null)
            {
                if (after.Length != 36 || !Guid.TryParseExact(after, "D", out var parsed) || parsed == Guid.Empty)
                    return ErrorFor("invalid_invitation_cursor");
                cursor = parsed;
            }
            var result = await service.ListBoardAsync(boardId, actor.Value, cursor, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().RequireRateLimiting("invitation");

        app.MapPost("/boards/{boardId:guid}/invitations", async (Guid boardId,
            CreateBoardInvitationRequest request, HttpContext context, BoardInvitationService service,
            CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            Guid? retryKey = null;
            if (context.Request.Headers.TryGetValue("Idempotency-Key", out var values))
            {
                if (values.Count != 1 || values[0]?.Length != 36 || !Guid.TryParseExact(values[0], "D", out var key) || key == Guid.Empty)
                    return ErrorFor("invalid_idempotency_key");
                retryKey = key;
            }
            // Invalid roles reach the command so current Board authority is checked first.
            var role = request.Role?.Trim().ToUpperInvariant() switch {
                "ADMIN" => StrataAI.Application.WorkManagement.BoardRole.Admin,
                "MEMBER" => StrataAI.Application.WorkManagement.BoardRole.Member,
                _ => (StrataAI.Application.WorkManagement.BoardRole)(-1),
            };
            var result = await service.CreateAsync(boardId, actor.Value, request.Email, role,
                context.TraceIdentifier, cancellationToken, retryKey);
            if (!result.Succeeded || result.Value is null) return ErrorFor(result.ErrorCode);
            var invitation = result.Value.Invitation;
            return Results.Created($"/boards/{boardId}/invitations/{invitation.Id}", new {
                invitation.Id, invitation.OrganizationId, Email = invitation.InvitedEmail,
                Surface = "INTERNAL", invitation.TargetRole, invitation.BoardTarget, invitation.ExpiresAt,
                InvitationToken = runtime.Mode == RuntimeMode.Demo && retryKey is null && !string.IsNullOrEmpty(result.Value.RawToken)
                    ? result.Value.RawToken : null,
            });
        }).RequireAuthorization().RequireRateLimiting("invitation");

        app.MapGet("/organizations/{organizationId:guid}/invitations", async (
            Guid organizationId, string? after, HttpContext context, InvitationHistoryService service,
            CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            Guid? cursor = null;
            if (after is not null)
            {
                if (after.Length != 36 || !Guid.TryParseExact(after, "D", out var parsed) || parsed == Guid.Empty)
                    return ErrorFor("invalid_invitation_cursor");
                cursor = parsed;
            }
            var result = await service.ListAsync(organizationId, actor.Value, cursor, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().RequireRateLimiting("invitation");

        app.MapPost(
                "/organizations/{organizationId:guid}/invitations",
                async (
                    Guid organizationId,
                    CreateInvitationRequest request,
                    HttpContext context,
                    IInvitationService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    Guid? retryKey = null;
                    if (context.Request.Headers.TryGetValue("Idempotency-Key", out var values))
                    {
                        if (values.Count != 1 || values[0]?.Length != 36 || !Guid.TryParseExact(values[0], "D", out var key) || key == Guid.Empty)
                            return ErrorFor("invalid_idempotency_key");
                        retryKey = key;
                    }

                    if (!TryParseSurface(request.Surface, out var surface))
                    {
                        return Problem(
                            StatusCodes.Status400BadRequest,
                            "invalid_invitation_surface",
                            "Invitation surface must be INTERNAL or PORTAL.");
                    }

                    var result = await service.CreateAsync(
                        organizationId,
                        userId.Value,
                        request.Email,
                        surface,
                        request.TargetRole,
                        context.TraceIdentifier,
                        cancellationToken,
                        retryKey);

                    if (!result.Succeeded || result.Value is null)
                    {
                        return ErrorFor(result.ErrorCode);
                    }

                    var invitation = result.Value.Invitation;
                    return Results.Created(
                        $"/organizations/{organizationId}/invitations/{invitation.Id}",
                        new CreateInvitationResponse(
                            invitation.Id,
                            invitation.OrganizationId,
                            invitation.InvitedEmail,
                            invitation.Surface.ToString().ToUpperInvariant(),
                            invitation.TargetRole,
                            invitation.ExpiresAt,
                            runtime.Mode == RuntimeMode.Demo && retryKey is null
                                ? result.Value.RawToken
                                : null));
                })
            .RequireAuthorization().RequireRateLimiting("invitation");

        app.MapGet(
                "/me/invitations",
                async (
                    string? after,
                    HttpContext context,
                    IInvitationService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    Guid? cursor = null;
                    if (after is not null)
                    {
                        if (after.Length != 36 || !Guid.TryParseExact(after, "D", out var parsed) || parsed == Guid.Empty)
                            return ErrorFor("invalid_invitation_cursor");
                        cursor = parsed;
                    }
                    var result = await service.ListPendingAsync(userId.Value, cursor, cancellationToken);
                    return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization().RequireRateLimiting("invitation");

        app.MapPost("/me/invitations/{invitationId:guid}/accept", async (Guid invitationId, HttpContext context,
            IInvitationService service, CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId is null) return Results.Unauthorized();
            var result = await service.AcceptPendingAsync(userId.Value, invitationId, context.TraceIdentifier, cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().RequireRateLimiting("invitation");

        app.MapPost("/invitations/review", async (AcceptInvitationRequest request, HttpContext context,
            IInvitationService service, CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId is null) return Results.Unauthorized();
            var result = await service.ReviewTokenAsync(userId.Value, request.Token ?? "", cancellationToken);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().RequireRateLimiting("invitation");

        // Bearer tokens belong in the request body, never in new recipient link paths.
        app.MapPost("/invitations/accept", async (AcceptInvitationRequest request, HttpContext context,
            IInvitationService service, CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 512)
                return ErrorFor("invalid_or_expired_invitation");
            var result = await service.AcceptAsync(userId.Value, request.Token, context.TraceIdentifier, cancellationToken);
            return result.Succeeded && result.Value is not null
                ? Results.Ok(new AcceptInvitationResponse(result.Value.InvitationId, result.Value.OrganizationId,
                    result.Value.Surface.ToString().ToUpperInvariant(), result.Value.TargetRole, result.Value.BoardTarget))
                : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().RequireRateLimiting("invitation");

        app.MapPost(
                "/invitations/{token}/accept",
                async (
                    string token,
                    HttpContext context,
                    IInvitationService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.AcceptAsync(
                        userId.Value,
                        token,
                        context.TraceIdentifier,
                        cancellationToken);

                    if (!result.Succeeded || result.Value is null)
                    {
                        return ErrorFor(result.ErrorCode);
                    }

                    return Results.Ok(
                        new AcceptInvitationResponse(
                            result.Value.InvitationId,
                            result.Value.OrganizationId,
                            result.Value.Surface.ToString().ToUpperInvariant(),
                            result.Value.TargetRole, result.Value.BoardTarget));
                })
            .RequireAuthorization().RequireRateLimiting("invitation");

        app.MapDelete(
                "/organizations/{organizationId:guid}/invitations/{invitationId:guid}",
                async (
                    Guid organizationId,
                    Guid invitationId,
                    HttpContext context,
                    IInvitationService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.RevokeAsync(
                        organizationId,
                        userId.Value,
                        invitationId,
                        context.TraceIdentifier,
                        cancellationToken);

                    return result.Succeeded
                        ? Results.NoContent()
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization().RequireRateLimiting("invitation");
    }

    private static Guid? GetUserId(HttpContext context)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static bool TryParseSurface(
        string value,
        out InvitationSurface surface)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "INTERNAL":
                surface = InvitationSurface.Internal;
                return true;
            case "PORTAL":
                surface = InvitationSurface.Portal;
                return true;
            default:
                surface = default;
                return false;
        }
    }

    private static IResult ErrorFor(string? errorCode) =>
        errorCode switch
        {
            "invalid_idempotency_key" => Problem(StatusCodes.Status400BadRequest, errorCode, "A nonempty UUID retry key is required."),
            "idempotency_key_reused" => Problem(StatusCodes.Status409Conflict, errorCode, "This retry key belongs to a different invitation request."),
            "idempotency_key_expired" => Problem(StatusCodes.Status409Conflict, errorCode, "The invitation acknowledgment has expired. This key cannot create another invitation."),
            "invalid_invitation_cursor" => Problem(StatusCodes.Status400BadRequest, errorCode, "A nonempty UUID invitation cursor is required."),
            "session_unavailable" => Problem(
                StatusCodes.Status401Unauthorized,
                errorCode,
                "Your session is no longer available. Sign in again."),
            "invitation_storage_unavailable" => Problem(
                StatusCodes.Status503ServiceUnavailable,
                errorCode,
                "The invitation change could not be confirmed. Retry shortly."),
            "ownership_change_requires_confirmation" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "An invitation cannot remove active organization ownership."),
            "insufficient_permission" => Problem(
                StatusCodes.Status403Forbidden,
                errorCode,
                "The invitation role cannot be granted by this account."),
            "invalid_email" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid invitation email is required."),
            "invalid_invitation_role" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The invitation role is not valid for that surface."),
            "invalid_or_expired_invitation" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The invitation is invalid, expired, already used, or does not match this account."),
            "account_unavailable" => Problem(
                StatusCodes.Status403Forbidden,
                errorCode,
                "The account cannot accept this invitation."),
            "board_not_found" => Problem(StatusCodes.Status404NotFound, errorCode, "The Board was not found or cannot be invited by this account."),
            "invitation_not_found" => Problem(
                StatusCodes.Status404NotFound,
                errorCode,
                "The invitation was not found."),
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
