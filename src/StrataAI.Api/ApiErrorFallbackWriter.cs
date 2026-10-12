using System.Text.Json;

namespace StrataAI.Api;

// Results.Problem otherwise falls back to JSON without customization when
// the default writer cannot negotiate the requested response media type.
public sealed class ApiErrorFallbackWriter : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public async ValueTask WriteAsync(ProblemDetailsContext context)
    {
        ApiErrorContract.Customize(context);
        await context.HttpContext.Response.WriteAsJsonAsync((object)context.ProblemDetails,
            options: (JsonSerializerOptions?)null, contentType: "application/problem+json",
            cancellationToken: context.HttpContext.RequestAborted);
    }
}
