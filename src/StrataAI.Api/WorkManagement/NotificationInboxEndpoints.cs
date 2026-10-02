using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private sealed record NotificationSelection(Guid[]? Ids);

    private static void MapNotificationEndpoints(WebApplication app)
    {
        app.MapGet("/organizations/{organizationId:guid}/notifications", async (Guid organizationId, string? after,
            HttpContext context, NotificationInboxService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ListAsync(organizationId, actor.Value, NotificationCursor.Parse(after), ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/organizations/{organizationId:guid}/notifications/{notificationId:guid}/read", async
            (Guid organizationId, Guid notificationId, HttpContext context, NotificationInboxService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.MarkReadAsync(organizationId, actor.Value, [notificationId], ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/organizations/{organizationId:guid}/notifications/read", async
            (Guid organizationId, NotificationSelection selection, HttpContext context, NotificationInboxService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.MarkReadAsync(organizationId, actor.Value, selection.Ids ?? [], ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
