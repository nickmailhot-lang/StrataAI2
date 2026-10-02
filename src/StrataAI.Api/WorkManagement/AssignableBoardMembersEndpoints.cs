using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapAssignableBoardMembersEndpoints(WebApplication app)
    {
        app.MapGet("/boards/{boardId:guid}/assignable-members", async (Guid boardId, string? after,
            HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            Guid? cursor = after is null ? null : Guid.TryParse(after, out var parsed) ? parsed : Guid.Empty;
            var result = await service.ListAssignableBoardMembersAsync(boardId, actor.Value, cursor, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
