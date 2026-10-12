using Microsoft.AspNetCore.Diagnostics;

namespace StrataAI.Api;

public static class ApiErrorContract
{
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        problem.Extensions.TryAdd("code", Code(status));
        problem.Extensions["message"] = problem.Title ?? Message(status);
        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    }

    public static async Task WriteEmptyError(StatusCodeContext context)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsHead(http.Request.Method)) return;
        var status = http.Response.StatusCode;
        await Results.Problem(statusCode: status, title: Message(status),
            extensions: new Dictionary<string, object?> { ["code"] = Code(status) }).ExecuteAsync(http);
    }

    private static string Code(int status) => status switch
    {
        400 => "invalid_request",
        401 => "authentication_required",
        403 => "access_unavailable",
        404 => "not_found",
        405 => "method_not_allowed",
        409 => "conflict",
        413 => "request_too_large",
        415 => "unsupported_media_type",
        429 => "rate_limited",
        _ => "request_failed",
    };

    private static string Message(int status) => status switch
    {
        400 => "The request is invalid. Check the input and try again.",
        401 => "Sign in to continue.",
        403 => "This action is unavailable.",
        404 => "The requested resource is unavailable.",
        405 => "This request method is unavailable.",
        409 => "The resource changed. Review its current state and try again.",
        413 => "The request exceeds the size limit.",
        415 => "The request content type is unsupported.",
        429 => "Too many requests. Wait before trying again.",
        _ => "The request could not be completed. Try again.",
    };
}
