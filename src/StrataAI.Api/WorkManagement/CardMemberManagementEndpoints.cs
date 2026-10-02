using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapCardMemberEndpoints(WebApplication app)
    {
        foreach (var assigned in new[] { true, false })
            app.MapMethods("/cards/{cardId:guid}/members/{userId:guid}", [assigned ? "PUT" : "DELETE"],
                async (Guid cardId, Guid userId, long version, HttpContext context, IWorkManagementService service, CancellationToken ct) =>
                {
                    var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
                    var result = await service.SetCardMemberAsync(cardId, userId, actor.Value, assigned, version, context.TraceIdentifier, ct);
                    return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
                }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
