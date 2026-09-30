using System.Security.Claims;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;

namespace StrataAI.Api.Auth;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(
        this WebApplication app,
        RuntimeDescriptor runtime,
        IdentityPolicy policy)
    {
        var auth = app.MapGroup("/auth");

        auth.MapPost(
            "/register",
            async (
                RegisterRequest request,
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                var result = await identityService.RegisterAsync(
                    request.Email,
                    request.Password,
                    request.DisplayName,
                    request.Locale,
                    request.Timezone,
                    context.TraceIdentifier,
                    cancellationToken);

                if (!result.Succeeded || result.Value is null)
                {
                    return ErrorFor(result.ErrorCode);
                }

                var verificationToken =
                    runtime.Mode == RuntimeMode.Demo
                        ? result.Value.VerificationToken
                        : null;

                return Results.Created(
                    "/me",
                    new RegistrationResponse(
                        result.Value.User,
                        verificationToken));
            });

        auth.MapPost(
            "/login",
            async (
                LoginRequest request,
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                var result = await identityService.LoginAsync(
                    request.Email,
                    request.Password,
                    context.TraceIdentifier,
                    cancellationToken);

                if (!result.Succeeded || result.Value is null)
                {
                    return ErrorFor(result.ErrorCode);
                }

                SetSessionCookie(
                    context,
                    result.Value.SessionToken,
                    result.Value.SessionExpiresAt,
                    runtime.Mode == RuntimeMode.Production);

                return Results.Ok(
                    new LoginResponse(
                        result.Value.User,
                        result.Value.SessionExpiresAt));
            });

        auth.MapPost(
            "/password/forgot",
            async (
                ForgotPasswordRequest request,
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                var outcome = await identityService.RequestPasswordResetAsync(
                    request.Email,
                    context.TraceIdentifier,
                    cancellationToken);

                return Results.Accepted(
                    value: new PasswordResetRequestResponse(
                        true,
                        runtime.Mode == RuntimeMode.Demo
                            ? outcome.ResetToken
                            : null));
            });

        auth.MapPost(
            "/password/reset",
            async (
                ResetPasswordRequest request,
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                var result = await identityService.ResetPasswordAsync(
                    request.Token,
                    request.NewPassword,
                    context.TraceIdentifier,
                    cancellationToken);

                return result.Succeeded && result.Value is not null
                    ? Results.Ok(result.Value)
                    : ErrorFor(result.ErrorCode);
            });

        auth.MapPost(
            "/verify-email",
            async (
                VerifyEmailRequest request,
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                var result = await identityService.VerifyEmailAsync(
                    request.Token,
                    context.TraceIdentifier,
                    cancellationToken);

                return result.Succeeded && result.Value is not null
                    ? Results.Ok(result.Value)
                    : ErrorFor(result.ErrorCode);
            });

        auth.MapPost(
                "/logout",
                async (
                    HttpContext context,
                    IIdentityService identityService,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    context.Request.Cookies.TryGetValue(
                        SessionAuthenticationMiddleware.CookieName,
                        out var rawToken);

                    await identityService.LogoutAsync(
                        rawToken ?? string.Empty,
                        userId.Value,
                        context.TraceIdentifier,
                        cancellationToken);

                    context.Response.Cookies.Delete(
                        SessionAuthenticationMiddleware.CookieName);

                    return Results.NoContent();
                })
            .RequireAuthorization();

        var me = app.MapGroup("/me").RequireAuthorization();

        me.MapGet(
            "",
            (HttpContext context) =>
            {
                return context.Items.TryGetValue(
                        SessionAuthenticationMiddleware.ProfileItemKey,
                        out var profile) &&
                    profile is UserProfile user
                        ? Results.Ok(user)
                        : Results.Unauthorized();
            });

        me.MapPatch(
            "",
            async (
                UpdateProfileRequest request,
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await identityService.UpdateProfileAsync(
                    userId.Value,
                    request.DisplayName,
                    request.AvatarUrl,
                    request.Locale,
                    request.Timezone,
                    context.TraceIdentifier,
                    cancellationToken);

                return result.Succeeded && result.Value is not null
                    ? Results.Ok(result.Value)
                    : ErrorFor(result.ErrorCode);
            });

        me.MapPost(
            "/deactivate",
            async (
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var deactivated = await identityService.DeactivateAsync(
                    userId.Value,
                    context.TraceIdentifier,
                    cancellationToken);

                if (!deactivated)
                {
                    return Results.NotFound();
                }

                context.Response.Cookies.Delete(
                    SessionAuthenticationMiddleware.CookieName);

                return Results.NoContent();
            });

        app.MapGet(
            "/api/auth/policy",
            () => Results.Ok(new
            {
                allowSelfRegistration = policy.AllowSelfRegistration,
                requireVerifiedEmail = policy.RequireVerifiedEmail,
                minimumPasswordLength = policy.MinimumPasswordLength,
            }));
    }

    private static Guid? GetUserId(HttpContext context)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static void SetSessionCookie(
        HttpContext context,
        string rawToken,
        DateTimeOffset expiresAt,
        bool secure)
    {
        context.Response.Cookies.Append(
            SessionAuthenticationMiddleware.CookieName,
            rawToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = expiresAt,
                IsEssential = true,
            });
    }

    private static IResult ErrorFor(string? errorCode) =>
        errorCode switch
        {
            "self_registration_disabled" => Problem(
                StatusCodes.Status403Forbidden,
                errorCode,
                "Self-registration is not enabled."),
            "invalid_email" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid email address is required."),
            "invalid_password" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The password does not meet the configured policy."),
            "invalid_display_name" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid display name is required."),
            "email_unavailable" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "The account cannot be created with the supplied email."),
            "email_verification_required" => Problem(
                StatusCodes.Status403Forbidden,
                errorCode,
                "Email verification is required."),
            "invalid_or_expired_token" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The token is invalid or expired."),
            "account_unavailable" => Problem(
                StatusCodes.Status401Unauthorized,
                errorCode,
                "Authentication failed."),
            _ => Problem(
                StatusCodes.Status401Unauthorized,
                "invalid_credentials",
                "Authentication failed."),
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
