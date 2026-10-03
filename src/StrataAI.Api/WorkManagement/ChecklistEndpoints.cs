using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapChecklistEndpoints(WebApplication app)
    {
        app.MapDelete("/cards/{cardId:guid}/checklists/{checklistId:guid}", async (Guid cardId, Guid checklistId, [Microsoft.AspNetCore.Mvc.FromBody] DeleteChecklistInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.DeleteAsync(cardId, checklistId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapDelete("/cards/{cardId:guid}/checklists/{checklistId:guid}/items/{itemId:guid}", async (Guid cardId, Guid checklistId, Guid itemId, [Microsoft.AspNetCore.Mvc.FromBody] DeleteChecklistItemInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.DeleteItemAsync(cardId, checklistId, itemId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPatch("/cards/{cardId:guid}/checklists/{checklistId:guid}/position", async (Guid cardId, Guid checklistId, ChecklistPositionInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ReorderAsync(cardId, checklistId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPatch("/cards/{cardId:guid}/checklists/{checklistId:guid}/items/{itemId:guid}/position", async (Guid cardId, Guid checklistId, Guid itemId, ChecklistItemPositionInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ReorderItemAsync(cardId, checklistId, itemId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPatch("/cards/{cardId:guid}/checklists/{checklistId:guid}/items/{itemId:guid}", async (Guid cardId, Guid checklistId, Guid itemId, UpdateChecklistItemInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.UpdateItemAsync(cardId, checklistId, itemId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/checklists/{checklistId:guid}/items", async (Guid cardId, Guid checklistId, string? after, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ListItemsAsync(cardId, checklistId, actor.Value, after, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/cards/{cardId:guid}/checklists/{checklistId:guid}/items", async (Guid cardId, Guid checklistId, CreateChecklistItemInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.CreateItemAsync(cardId, checklistId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/checklists", async (Guid cardId, string? after, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ListAsync(cardId, actor.Value, after, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPatch("/cards/{cardId:guid}/checklists/{checklistId:guid}", async (Guid cardId, Guid checklistId, RenameChecklistInput input, HttpContext context, ChecklistService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.RenameAsync(cardId, checklistId, actor.Value, input, context.TraceIdentifier, ct);
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
