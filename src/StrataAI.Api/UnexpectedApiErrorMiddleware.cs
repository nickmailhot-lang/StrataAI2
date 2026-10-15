namespace StrataAI.Api;

public sealed class UnexpectedApiErrorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILogger<UnexpectedApiErrorMiddleware> logger)
    {
        try
        {
            await next(context);
        }
        catch (Exception) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            // The exception itself can contain provider secrets or submitted input.
            ApiDiagnostics.Error(logger, new EventId(5000, "UnexpectedApiError"),
                "An API request failed. CorrelationId: {CorrelationId}", context.TraceIdentifier);
            context.Response.Clear();
            context.Response.Headers[CorrelationIdMiddleware.HeaderName] = context.TraceIdentifier;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsHead(context.Request.Method)) return;
            await Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "The request could not be completed. Try again.",
                extensions: new Dictionary<string, object?> { ["code"] = "internal_error" })
                .ExecuteAsync(context);
        }
    }
}
