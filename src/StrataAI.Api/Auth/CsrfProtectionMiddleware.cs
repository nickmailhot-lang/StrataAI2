namespace StrataAI.Api.Auth;

/// <summary>
/// Same-origin JSON clients prove intent using a non-simple request header.
/// Cross-origin browser requests cannot supply this header without a successful
/// CORS preflight; this API deliberately grants no cross-origin CORS access.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-StrataAI-Request";

    public async Task InvokeAsync(HttpContext context, ILogger<CsrfProtectionMiddleware> logger)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) || context.GetEndpoint() is null)
        {
            await next(context);
            return;
        }

        if (request.Headers[HeaderName].ToString() != "1" ||
            request.Headers["Sec-Fetch-Site"].ToString() is "cross-site" or "same-site")
        {
            logger.LogWarning("CSRF protection rejected a mutation. CorrelationId={CorrelationId}", context.TraceIdentifier);
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The request could not be verified. Reload and retry.",
                extensions: new Dictionary<string, object?> { ["code"] = "csrf_rejected" })
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
