using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapCardCommentEndpoints(WebApplication app)
    {
        app.MapGet("/cards/{cardId:guid}/comments", async (Guid cardId, string? after, HttpContext context, CardCommentService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ListAsync(cardId, actor.Value, after, ct);
            context.Response.Headers.CacheControl = "no-store";
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/cards/{cardId:guid}/comments", async (Guid cardId, CreateCardCommentInput input, HttpContext context, CardCommentService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.CreateAsync(cardId, actor.Value, input, context.TraceIdentifier, ct);
            context.Response.Headers.CacheControl = "no-store";
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPatch("/cards/{cardId:guid}/comments/{commentId:guid}", async (Guid cardId, Guid commentId, EditCardCommentInput input, HttpContext context, CardCommentService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.EditAsync(cardId, commentId, actor.Value, input, context.TraceIdentifier, ct);
            context.Response.Headers.CacheControl = "no-store";
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapDelete("/cards/{cardId:guid}/comments/{commentId:guid}", async (Guid cardId, Guid commentId,
            [Microsoft.AspNetCore.Mvc.FromBody] DeleteCardCommentInput input, HttpContext context, CardCommentService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.DeleteAsync(cardId, commentId, actor.Value, input, context.TraceIdentifier, ct);
            context.Response.Headers.CacheControl = "no-store";
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
