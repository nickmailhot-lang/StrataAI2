using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapCardCommentEndpoints(WebApplication app)
    {
        app.MapGet("/cards/{cardId:guid}/mention-options", async (Guid cardId, HttpContext context, CardMentionOptionsService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var query = context.Request.Query;
            if (query["prefix"].Count > 1) return ErrorFor("mention_prefix_invalid");
            if (query["after"].Count > 1) return ErrorFor("invalid_mention_cursor");
            var result = await service.ListAsync(cardId, actor.Value, query["prefix"].ToString(),
                query.ContainsKey("after") ? query["after"].ToString() : null, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
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
