using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StrataAI.Application.Identity;

namespace StrataAI.Api.Auth;

public static class SessionAuthenticationDefaults
{
    public const string Scheme = "StrataAI.Session";
    public const string CookieName = "strataai_session";
    public const string ProfileItemKey = "StrataAI.UserProfile";
}

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IIdentityService identityService,
    ISecureTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(
                SessionAuthenticationDefaults.CookieName,
                out var rawSessionToken) ||
            string.IsNullOrWhiteSpace(rawSessionToken))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await identityService.AuthenticateSessionAsync(
            rawSessionToken,
            Context.RequestAborted);

        if (session is null)
        {
            return AuthenticateResult.NoResult();
        }

        var user = session.User;
        Context.Items[typeof(ICommandActorContext)] = tokens.Hash(rawSessionToken);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        Context.Items[SessionAuthenticationDefaults.ProfileItemKey] =
            new UserProfile(
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

        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }
}
