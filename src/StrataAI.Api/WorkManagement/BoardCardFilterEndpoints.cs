using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapBoardCardFilterEndpoints(WebApplication app)
    {
        app.MapGet("/boards/{boardId:guid}/cards", async (Guid boardId, string? keyword, string? labels, string? members, string? match, string? after, string? completion, string? due, string? activity,
            HttpContext context, IWorkManagementService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context);
            // Invalid input still goes through Board admission before revealing a validation error.
            var ids = labels is null or "" ? Array.Empty<Guid>() : labels.Split(',').Take(26).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).ToArray();
            var memberIds = members is null or "" ? Array.Empty<Guid>() : members.Split(',').Take(26).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).ToArray();
            Guid? cursor = after is null ? null : Guid.TryParse(after, out var parsed) ? parsed : Guid.Empty;
            var result = await service.FilterBoardCardsAsync(boardId, actor, keyword, ids, match, cursor, ct, memberIds, completion, due, activity);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).AllowAnonymous().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPost("/boards/{boardId:guid}/cards/filter-change", async (Guid boardId, string? change, string? keyword, string? labels,
            string? members, string? match, string? completion, string? due, string? activity, HttpContext context,
            IWorkManagementService service, IWorkCommandContext commands, BoardFilterInteractionChangeProducer interactions, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            // Bind the client intent to its admitted account before dispatch.
            // A cookie change must not create an event for another account
            // using the previous account's session-local criteria.
            if (!context.Request.Headers.TryGetValue("X-StrataAI-Expected-Actor", out var expected) || expected.Count != 1
                || expected[0]?.Length != 36 || !Guid.TryParseExact(expected[0], "D", out var expectedActor) || expectedActor == Guid.Empty)
                return ErrorFor("invalid_board_filter");
            if (expectedActor != actor.Value) return ErrorFor("session_unavailable");
            var ids = labels is null or "" ? Array.Empty<Guid>() : labels.Split(',').Take(26).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).ToArray();
            var memberIds = members is null or "" ? Array.Empty<Guid>() : members.Split(',').Take(26).Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).ToArray();
            // Validation and current Board disclosure admission precede source
            // production. This read boundary finishes before the identity unit.
            var result = await service.FilterBoardCardsAsync(boardId, actor, keyword, ids, match, null, ct, memberIds, completion, due, activity);
            if (!result.Succeeded || result.Value is null) return ErrorFor(result.ErrorCode);
            var action = (change ?? "").ToLowerInvariant();
            var text = (keyword ?? "").Trim(); var mode = (match ?? "all").ToLowerInvariant();
            var completionMode = (completion ?? "all").ToLowerInvariant(); var dueMode = (due ?? "all").ToLowerInvariant();
            var activityMode = (activity ?? "all").ToLowerInvariant();
            if (action is not ("apply" or "clear") || context.Request.Query.ContainsKey("after")
                || context.Request.Query.Keys.Any(key => key is not ("change" or "keyword" or "labels" or "members" or "match" or "completion" or "due" or "activity"))
                || context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                || context.Request.ContentLength is > 0 || context.Request.Headers.TransferEncoding.Count > 0
                || action == "clear" && (text.Length > 0 || ids.Length > 0 || memberIds.Length > 0 || mode != "all"
                    || completionMode != "all" || dueMode != "all" || activityMode != "all")) return ErrorFor("invalid_board_filter");
            if (commands.IdempotencyKey is not { } key) return ErrorFor("invalid_idempotency_key");
            var fingerprint = WorkCommand.Create(actor.Value, key, "BoardFilterChanged", boardId,
                new { change = action, keyword = text, labels = ids.Order().ToArray(), members = memberIds.Order().ToArray(), match = mode,
                    completion = completionMode, due = dueMode, activity = activityMode }, "board_not_found").Fingerprint.ToLowerInvariant();
            var source = await interactions.ProduceAsync(actor.Value, result.Value.OrganizationId, result.Value.BoardId, key, fingerprint, ct);
            if (!source.Succeeded || source.Value is null)
                return ErrorFor(source.ErrorCode == "identity_storage_unavailable" ? "work_storage_unavailable" : source.ErrorCode);
            return Results.Ok(source.Value);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
