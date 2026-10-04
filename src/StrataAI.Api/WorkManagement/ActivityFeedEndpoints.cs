using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapActivityFeedEndpoints(WebApplication app)
    {
        app.MapGet("/boards/{boardId:guid}/activity", async (Guid boardId, HttpContext context, ActivityFeedService service, CancellationToken ct)
            => await ReadActivity(ActivityTargetKind.Board, boardId, context, service, ct))
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/activity", async (Guid cardId, HttpContext context, ActivityFeedService service, CancellationToken ct)
            => await ReadActivity(ActivityTargetKind.Card, cardId, context, service, ct))
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
    private static async Task<IResult> ReadActivity(ActivityTargetKind kind, Guid id, HttpContext context, ActivityFeedService service, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
        var query = context.Request.Query;
        // Root admission precedes malformed/expired cursor disclosure.
        var after = query["after"].Count > 1 ? "invalid" : query.ContainsKey("after") ? query["after"].ToString() : null;
        var result = await service.ReadAsync(kind, id, actor.Value, after, ct);
        return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
    }
}
