using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;
public static partial class WorkManagementEndpoints
{
    private sealed record CopyBoardRequest(string Name, long Version);
    private static void MapBoardCopyEndpoints(WebApplication app)
    {
        app.MapPost("/boards/{boardId:guid}/copy", async (Guid boardId, CopyBoardRequest request,
            HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.CopyBoardAsync(boardId, actor.Value, request.Name, request.Version, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Created($"/boards/{result.Value.Id}", result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
