using System.Globalization;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapWatchEndpoints(WebApplication app)
    {
        app.MapGet("/watch/{entityType}/{entityId:guid}", async (string entityType, Guid entityId,
            HttpContext context, WatchSubscriptionService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.GetAsync(entityType, entityId, actor.Value, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapMethods("/watch/{entityType}/{entityId:guid}", ["PUT", "DELETE"], async (string entityType, Guid entityId,
            string? version, HttpContext context, WatchSubscriptionService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var parsed = version is not null && long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1;
            var result = await service.SetAsync(entityType, entityId, actor.Value, HttpMethods.IsPut(context.Request.Method),
                parsed, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
