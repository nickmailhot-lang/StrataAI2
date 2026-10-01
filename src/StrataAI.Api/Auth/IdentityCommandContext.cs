using StrataAI.Application.Identity;

namespace StrataAI.Api.Auth;

internal sealed class HttpIdentityCommandContext(IHttpContextAccessor contexts) : IIdentityCommandContext
{
    public Guid? IdempotencyKey => contexts.HttpContext?.Items[typeof(IdentityProfileReplay)] is Guid key ? key : null;
}

public sealed class IdentityProfileIdempotencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPatch(context.Request.Method) && string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/me", StringComparison.OrdinalIgnoreCase)
            && context.Request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            if (values.Count != 1 || values[0]?.Length != 36 || !Guid.TryParseExact(values[0], "D", out var key) || key == Guid.Empty)
            {
                await Results.Problem(statusCode: 400, title: "A nonempty UUID retry key is required.",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_idempotency_key" }).ExecuteAsync(context);
                return;
            }
            context.Items[typeof(IdentityProfileReplay)] = key;
        }
        await next(context);
    }
}
