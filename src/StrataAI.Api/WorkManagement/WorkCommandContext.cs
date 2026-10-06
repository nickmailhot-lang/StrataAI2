using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

internal sealed class HttpWorkCommandContext(IHttpContextAccessor contexts) : IWorkCommandContext
{
    public Guid? IdempotencyKey => contexts.HttpContext?.Items[typeof(WorkCommand)] is Guid key ? key : null;
}

public sealed class WorkIdempotencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var workPath = new[] { "/boards", "/lists", "/cards", "/labels", "/watch", "/attachments" }.Any(path => context.Request.Path.StartsWithSegments(path));
        var navigationObservation = (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText
            == "/navigation/observations";
        if (navigationObservation) context.Response.Headers.CacheControl = "private, no-store";
        var notificationRead = (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText is
            "/organizations/{organizationId:guid}/notifications/{notificationId:guid}/read" or
            "/organizations/{organizationId:guid}/notifications/read";
        var mutation = HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method);
        if ((workPath || notificationRead || navigationObservation) && mutation && context.Request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            if (values.Count != 1 || values[0]?.Length != 36 || !Guid.TryParseExact(values[0], "D", out var key) || key == Guid.Empty)
            {
                if (BoardSharingTelemetry.Operation(context) is not null)
                    BoardSharingTelemetry.SetError(context, "invalid_idempotency_key");
                await Results.Problem(statusCode: 400, title: "A nonempty UUID retry key is required.",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_idempotency_key" }).ExecuteAsync(context);
                return;
            }
            context.Items[typeof(WorkCommand)] = key;
        }
        await next(context);
    }
}
