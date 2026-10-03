using System.Globalization;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapCardReminderEndpoints(WebApplication app)
    {
        app.MapGet("/cards/{cardId:guid}/reminder", async (Guid cardId, HttpContext context,
            CardReminderService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.GetAsync(cardId, actor.Value, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPut("/cards/{cardId:guid}/reminder", async (Guid cardId, CardReminderInput input, HttpContext context,
            CardReminderService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.SetAsync(cardId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapDelete("/cards/{cardId:guid}/reminder", async (Guid cardId, string? version, string? cardVersion,
            HttpContext context, CardReminderService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            static long Parse(string? value) => value is not null && long.TryParse(value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number) ? number : -1;
            var result = await service.SetAsync(cardId, actor.Value, new(null, false, Parse(cardVersion), Parse(version)), context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
