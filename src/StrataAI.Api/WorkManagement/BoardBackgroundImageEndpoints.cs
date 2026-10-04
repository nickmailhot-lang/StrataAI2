using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;
public static partial class WorkManagementEndpoints
{
    private static void MapBoardBackgroundImageEndpoints(WebApplication app)
    {
        if (!app.Services.GetRequiredService<AttachmentUploadAvailability>().Enabled) return;
        app.MapPost("/boards/{boardId:guid}/background/image", async (Guid boardId, SelectBoardBackgroundImageInput input,
            HttpContext context, BoardBackgroundImageSelectionService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.SelectAsync(boardId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/boards/{boardId:guid}/background/image", async (Guid boardId, long? boardVersion, HttpContext context,
            BoardBackgroundImageReadService service, BoardBackgroundImageAdmissionService admission, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store"; var actor = GetUserId(context);
            var result = await service.PrepareAsync(boardId, actor, ct, boardVersion);
            return result.Succeeded && result.Value is not null ? new BoardBackgroundImageResult(result.Value, admission, actor) : ErrorFor(result.ErrorCode);
        }).AllowAnonymous().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
