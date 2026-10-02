using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapBoardCardFilterEndpoints(WebApplication app)
    {
        app.MapGet("/boards/{boardId:guid}/cards", async (Guid boardId, string? keyword, string? labels, string? match, string? after,
            HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            // Invalid input still goes through Board admission before revealing a validation error.
            var ids = labels is null or "" ? Array.Empty<Guid>() : labels.Split(',').Take(26).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).ToArray();
            Guid? cursor = after is null ? null : Guid.TryParse(after, out var parsed) ? parsed : Guid.Empty;
            var result = await service.FilterBoardCardsAsync(boardId, actor.Value, keyword, ids, match, cursor, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
