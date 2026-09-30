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
                        cancellationToken);

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
                            runtime.Mode == RuntimeMode.Demo
                                ? result.Value.RawToken
                                : null));
                })
            .RequireAuthorization();

        app.MapGet(
                "/me/invitations",
                async (
                    HttpContext context,
                    IInvitationService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return Results.Ok(
                        await service.ListPendingAsync(
                            userId.Value,
                            cancellationToken));
                })
            .RequireAuthorization();

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
                            result.Value.TargetRole));
                })
            .RequireAuthorization();

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
            .RequireAuthorization();
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
