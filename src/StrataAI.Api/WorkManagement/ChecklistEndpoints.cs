using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapChecklistEndpoints(WebApplication app)
    {
        app.MapGet("/cards/{cardId:guid}/checklists", async (Guid cardId, string? after, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ListAsync(cardId, actor.Value, after, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/cards/{cardId:guid}/checklists", async (Guid cardId, CreateChecklistInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.CreateAsync(cardId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
