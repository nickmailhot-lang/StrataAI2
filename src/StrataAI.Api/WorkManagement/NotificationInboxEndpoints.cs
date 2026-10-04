using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private sealed record NotificationSelection(Guid[]? Ids);

    private static void MapNotificationEndpoints(WebApplication app)
    {
        app.MapGet("/organizations/{organizationId:guid}/notifications/sync", async (Guid organizationId, string? after,
            HttpContext context, NotificationInboxService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var cursor = after is null ? 0 : after.Length is > 0 and <= 19 && after.All(c => c is >= '0' and <= '9') &&
                long.TryParse(after, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed : -1;
            var result = await service.ReadEventsAsync(organizationId, actor.Value, cursor, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/organizations/{organizationId:guid}/notifications", async (Guid organizationId, string? after,
            HttpContext context, NotificationInboxService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ListAsync(organizationId, actor.Value, NotificationCursor.Parse(after), ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/organizations/{organizationId:guid}/notifications/{notificationId:guid}/read", async
            (Guid organizationId, Guid notificationId, HttpContext context, NotificationInboxService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.MarkReadAsync(organizationId, actor.Value, [notificationId], ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/organizations/{organizationId:guid}/notifications/read", async
            (Guid organizationId, NotificationSelection selection, HttpContext context, NotificationInboxService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.MarkReadAsync(organizationId, actor.Value, selection.Ids ?? [], ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
