using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapBoardCardFilterEndpoints(WebApplication app)
    {
        app.MapGet("/boards/{boardId:guid}/cards", async (Guid boardId, string? keyword, string? labels, string? members, string? match, string? after, string? completion, string? due, string? activity,
            HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context);
            // Invalid input still goes through Board admission before revealing a validation error.
            var ids = labels is null or "" ? Array.Empty<Guid>() : labels.Split(',').Take(26).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).ToArray();
            var memberIds = members is null or "" ? Array.Empty<Guid>() : members.Split(',').Take(26).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).ToArray();
            Guid? cursor = after is null ? null : Guid.TryParse(after, out var parsed) ? parsed : Guid.Empty;
            var result = await service.FilterBoardCardsAsync(boardId, actor, keyword, ids, match, cursor, ct, memberIds, completion, due, activity);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).AllowAnonymous().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
