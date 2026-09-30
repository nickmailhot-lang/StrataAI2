using System.Security.Claims;
using StrataAI.Application.Identity;

namespace StrataAI.Api.Auth;

public sealed class SessionAuthenticationMiddleware(RequestDelegate next)
{
    public const string CookieName = "strataai_session";
    public const string ProfileItemKey = "StrataAI.UserProfile";

    public async Task InvokeAsync(
        HttpContext context,
        IIdentityService identityService)
    {
        if (context.Request.Cookies.TryGetValue(
                CookieName,
                out var rawSessionToken) &&
            !string.IsNullOrWhiteSpace(rawSessionToken))
        {
            var session = await identityService.AuthenticateSessionAsync(
                rawSessionToken,
                context.RequestAborted);

            if (session is not null)
            {
                var user = session.User;
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.DisplayName),
                    new Claim(ClaimTypes.Email, user.Email),
                };

                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, "StrataAI.Session"));

                context.Items[ProfileItemKey] = new UserProfile(
                    user.Id,
                    user.Email,
                    user.DisplayName,
                    user.AvatarUrl,
                    user.Locale,
                    user.Timezone,
                    user.Status,
                    user.EmailVerified,
                    user.CreatedAt,
                    user.UpdatedAt,
                    user.Version);
            }
        }

        await next(context);
    }
}
