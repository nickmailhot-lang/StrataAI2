using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapLabelEndpoints(WebApplication app)
    {
        foreach (var assigned in new[] { true, false })
        {
            app.MapMethods("/cards/{cardId:guid}/labels/{labelId:guid}", [assigned ? "PUT" : "DELETE"],
                async (Guid cardId, Guid labelId, long version, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
                {
                    var actor = GetUserId(context);
                    if (actor is null) return Results.Unauthorized();
                    var result = await service.SetCardLabelAsync(cardId, labelId, actor.Value, assigned, version, context.TraceIdentifier, ct);
                    return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
                }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        }
        app.MapGet("/boards/{boardId:guid}/labels", async (Guid boardId, string? after, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            Guid? cursor = after is null ? null : Guid.TryParse(after, out var parsed) ? parsed : Guid.Empty;
            var result = await service.ListLabelsAsync(boardId, actor.Value, cursor, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/boards/{boardId:guid}/labels", async (Guid boardId, CreateLabelRequest request, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            var result = await service.CreateLabelAsync(boardId, actor.Value, request.Name, request.Color, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Created($"/labels/{result.Value.Id}", result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPatch("/labels/{labelId:guid}", async (Guid labelId, UpdateLabelRequest request, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            var result = await service.UpdateLabelAsync(labelId, actor.Value, request.Name, request.Color, request.Rank, request.Version, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapDelete("/labels/{labelId:guid}", async (Guid labelId, long version, bool? confirmed, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            var result = await service.DeleteLabelAsync(labelId, actor.Value, version, confirmed == true, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}

public sealed record CreateLabelRequest(string Name, string Color);
public sealed record UpdateLabelRequest(string Name, string Color, string? Rank, long Version);
