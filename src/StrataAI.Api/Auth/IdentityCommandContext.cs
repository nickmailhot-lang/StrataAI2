using StrataAI.Application.Identity;

namespace StrataAI.Api.Auth;

internal sealed class HttpIdentityCommandContext(IHttpContextAccessor contexts, ISecureTokenService tokens) : IIdentityCommandContext
{
    public Guid? IdempotencyKey => contexts.HttpContext?.Items[typeof(IIdentityCommandContext)] is Guid key ? key : null;
    public string? RevocationSessionTokenHash => contexts.HttpContext?.Request.Cookies.TryGetValue(SessionAuthenticationDefaults.CookieName, out var raw) == true
        && !string.IsNullOrWhiteSpace(raw) ? tokens.Hash(raw) : null;
}

public sealed class IdentityProfileIdempotencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimEnd('/');
        var supported = (HttpMethods.IsPatch(context.Request.Method) && string.Equals(path, "/me", StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsPost(context.Request.Method) && (string.Equals(path, "/auth/logout", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/auth/login", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/auth/register", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/me/deactivate", StringComparison.OrdinalIgnoreCase)));
        if (supported
            && context.Request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            if (values.Count != 1 || values[0]?.Length != 36 || !Guid.TryParseExact(values[0], "D", out var key) || key == Guid.Empty)
            {
                await Results.Problem(statusCode: 400, title: "A nonempty UUID retry key is required.",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_idempotency_key" }).ExecuteAsync(context);
                return;
            }
            context.Items[typeof(IIdentityCommandContext)] = key;
        }
        await next(context);
    }
}
