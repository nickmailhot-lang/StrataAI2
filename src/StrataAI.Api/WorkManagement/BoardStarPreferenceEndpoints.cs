using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapBoardStarPreferenceEndpoints(WebApplication app)
    {
        app.MapGet("/boards/{boardId:guid}/star",
        async (Guid boardId, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.GetStarAsync(boardId, actor.Value, ct);
            return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization();
        app.MapGet("/boards/{boardId:guid}/star/events",
            async (Guid boardId, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
                var after = !context.Request.Query.ContainsKey("after") ? 0 :
                    long.TryParse(context.Request.Query["after"], out var parsed) ? parsed : -1;
                var result = await service.GetStarEventsAsync(boardId,actor.Value,after,ct);
                return result.Succeeded ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
            }).RequireAuthorization();
    }
}
