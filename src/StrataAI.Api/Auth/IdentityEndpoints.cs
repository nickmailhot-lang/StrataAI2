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
        var auth = app.MapGroup("/auth").RequireRateLimiting("auth");

        auth.MapPost(
            "/register",
            async (
                RegisterRequest request,
                HttpContext context,
                IIdentityService identityService,
                CancellationToken cancellationToken) =>
            {
                if (runtime.Mode==RuntimeMode.Production && (policy.AllowSelfRegistration || request.InvitationToken is not null) && policy.RequireVerifiedEmail && !policy.EmailDeliveryEnabled)
                    return DeliveryUnavailable();
                var result = await identityService.RegisterAsync(
                    request.Email,
                    request.Password,
                    request.DisplayName,
                    request.Locale,
                    request.Timezone,
                    context.TraceIdentifier,
                    cancellationToken,
                    request.InvitationToken);

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
                if (runtime.Mode==RuntimeMode.Production && !policy.EmailDeliveryEnabled)
                    return DeliveryUnavailable();
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

        auth.MapPost("/verification/resend",async (ForgotPasswordRequest request,HttpContext context,IIdentityService identityService,CancellationToken cancellationToken)=>
        {
            if (runtime.Mode==RuntimeMode.Production && !policy.EmailDeliveryEnabled) return DeliveryUnavailable();
            var token=await identityService.RequestEmailVerificationAsync(request.Email,context.TraceIdentifier,cancellationToken);
            return Results.Accepted(value:new { accepted=true,verificationToken=runtime.Mode==RuntimeMode.Demo ? token : null });
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
                    IIdentityCommandContext commandContext,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (!ExpectedAccountMatches(context, userId)) return ErrorFor("session_unavailable");
                    if (userId is null && commandContext.IdempotencyKey is null)
                    {
                        return Results.Unauthorized();
                    }

                    context.Request.Cookies.TryGetValue(
                        SessionAuthenticationDefaults.CookieName,
                        out var rawToken);

                    var result = await identityService.LogoutAsync(
                        rawToken ?? string.Empty,
                        userId ?? Guid.Empty,
                        context.TraceIdentifier,
                        cancellationToken);

                    if (!result.Succeeded) return ErrorFor(result.ErrorCode);

                    context.Response.Cookies.Delete(
                        SessionAuthenticationDefaults.CookieName);

                    return Results.NoContent();
                })
            .AllowAnonymous(); // Only the receipt capability can admit a revoked proof; ordinary calls still require the user above.

        var me = app.MapGroup("/me").RequireAuthorization();

        me.MapGet("/mention-handle", async (HttpContext context, UserMentionHandleService handles, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var user = GetUserId(context);
            if (user is null) return Results.Unauthorized();
            var result = await handles.GetAsync(user.Value, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });
        me.MapPatch("/mention-handle", async (ClaimMentionHandleInput request, HttpContext context,
            UserMentionHandleService handles, IIdentityCommandContext commands, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var user = GetUserId(context);
            if (user is null) return Results.Unauthorized();
            if (!ExpectedAccountMatches(context, user)) return ErrorFor("session_unavailable");
            if (commands.IdempotencyKey is not { } key) return ErrorFor("invalid_idempotency_key");
            var result = await handles.ClaimAsync(user.Value, key, request, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        me.MapGet("/sync", async (long? after, HttpContext context, IIdentityService identityService,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId is null) return Results.Unauthorized();
            var result = await identityService.ReadEventsAsync(userId.Value, after, cancellationToken);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        });

        me.MapGet(
            "",
            (HttpContext context) =>
            {
                return context.Items.TryGetValue(
                        SessionAuthenticationDefaults.ProfileItemKey,
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

                if (!ExpectedAccountMatches(context, userId)) return ErrorFor("session_unavailable");

                var result = await identityService.UpdateProfileAsync(
                    userId.Value,
                    request.DisplayName,
                    request.AvatarUrl,
                    request.Locale,
                    request.Timezone,
                    request.Version,
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
                IIdentityCommandContext commandContext,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(context);
                if (!ExpectedAccountMatches(context, userId)) return ErrorFor("session_unavailable");
                if (userId is null && commandContext.IdempotencyKey is null)
                {
                    return Results.Unauthorized();
                }

                var deactivated = await identityService.DeactivateAsync(
                    userId ?? Guid.Empty,
                    context.TraceIdentifier,
                    cancellationToken);

                if (!deactivated.Succeeded)
                {
                    return ErrorFor(deactivated.ErrorCode);
                }

                context.Response.Cookies.Delete(
                    SessionAuthenticationDefaults.CookieName);

                return Results.NoContent();
            }).AllowAnonymous(); // GET/PATCH /me retain the group's normal authorization.

        app.MapGet(
            "/api/auth/policy",
            () => Results.Ok(new
            {
                allowSelfRegistration = policy.AllowSelfRegistration,
                requireVerifiedEmail = policy.RequireVerifiedEmail,
                minimumPasswordLength = policy.MinimumPasswordLength,
            }));
    }

    private static bool ExpectedAccountMatches(HttpContext context, Guid? actor)
    {
        if (!context.Request.Headers.TryGetValue("X-StrataAI-Expected-User", out var values)) return true;
        if (values.Count != 1 || !Guid.TryParse(values[0], out var expected) || expected == Guid.Empty) return false;
        // This is an intent fence, not authentication. Revoked-session retries
        // still require the original opaque session and existing receipt proof.
        return actor is null || actor == expected;
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
            SessionAuthenticationDefaults.CookieName,
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

    private static IResult DeliveryUnavailable() => Results.Problem(
        statusCode:503,title:"Password recovery and verification email are temporarily unavailable.",
        extensions:new Dictionary<string,object?> { ["code"]="identity_delivery_unavailable" });

    private static IResult ErrorFor(string? errorCode) =>
        errorCode switch
        {
            "invalid_idempotency_key" => Problem(StatusCodes.Status400BadRequest, errorCode, "A nonempty UUID retry key is required."),
            "mention_handle_invalid" => Problem(StatusCodes.Status400BadRequest, errorCode, "Use 3–40 letters, digits or underscores, beginning with a letter. Reserved names cannot be chosen."),
            "mention_handle_unavailable" => Problem(StatusCodes.Status409Conflict, errorCode, "This handle or original handle-change acknowledgment is unavailable. Review the current account setting."),
            "mention_handle_claim_refused" => Problem(StatusCodes.Status409Conflict, errorCode, "This account has reached its handle reservation limit. Choose a previously owned handle."),
            "invalid_correlation_id" => Problem(StatusCodes.Status400BadRequest, errorCode, "A valid request identifier is required."),
            "idempotency_key_expired" => Problem(StatusCodes.Status409Conflict, errorCode, "This account-change attempt is no longer available."),
            "identity_retry_key_unavailable" => Problem(StatusCodes.Status503ServiceUnavailable, errorCode, "This retry could not be confirmed. Contact support before starting another attempt."),
            "idempotency_key_reused" => Problem(StatusCodes.Status409Conflict, errorCode, "This retry key was already used for another account change."),
            "invalid_identity_cursor" => Problem(StatusCodes.Status400BadRequest, errorCode, "A valid account event cursor is required."),
            "session_unavailable" => Problem(StatusCodes.Status401Unauthorized, errorCode, "Your session is no longer available. Sign in again."),
            "identity_storage_unavailable" => Problem(StatusCodes.Status503ServiceUnavailable, errorCode, "The account change could not be confirmed. Retry shortly."),
            "invalid_or_expired_invitation" => Problem(StatusCodes.Status400BadRequest, errorCode,
                "The invitation is invalid, expired, used, or does not match this registration."),
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
            "invalid_version" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The current profile version is required."),
            "version_conflict" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "Your profile changed elsewhere. Load the latest profile before saving again."),
            "invalid_avatar_url" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "Avatar must be an HTTPS image URL without credentials."),
            "invalid_locale" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid regional locale is required."),
            "invalid_timezone" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid timezone is required."),
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
            "organization_owner_required" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "Another active owner is required before this account can be deactivated."),
            "ownership_changed" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "Organization ownership changed. Retry account deactivation after reviewing current access."),
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
