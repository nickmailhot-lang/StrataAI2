using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapCardDateEndpoints(WebApplication app)
    {
        app.MapPatch("/boards/{boardId:guid}/date-policy", async (Guid boardId, BoardDatePolicyInput input,
            HttpContext context, BoardDatePolicyService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.SetAsync(boardId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPatch("/cards/{cardId:guid}/dates", async (Guid cardId, CardDatesInput input,
            HttpContext context, CardDateService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.SetAsync(cardId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
