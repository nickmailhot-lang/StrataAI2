using Microsoft.Extensions.Primitives;

namespace StrataAI.Api;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(
        HttpContext context,
        ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId =
            context.Request.Headers.TryGetValue(HeaderName, out StringValues supplied)
            && !StringValues.IsNullOrEmpty(supplied)
                ? supplied.ToString()
                : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
        }))
        {
            await next(context);
        }
    }
}
