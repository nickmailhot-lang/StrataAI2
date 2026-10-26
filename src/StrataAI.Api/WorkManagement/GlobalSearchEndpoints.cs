using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapGlobalSearchEndpoints(WebApplication app)
    {
        app.MapGet("/search", async (string? q, string? label, string? member, string? match, string? scope, string? after,
            HttpContext context, GlobalSearchService service, SearchInteractionEventProducer interactions, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            // Existing clients may omit the header. Browser reads bind to the
            // freshly checked actor before criteria are evaluated or recorded.
            if (context.Request.Headers.TryGetValue("X-StrataAI-Expected-Actor", out var expected)
                && (expected.Count != 1 || !Guid.TryParse(expected[0], out var expectedActor)
                    || expectedActor == Guid.Empty || expectedActor != actor.Value))
                return ErrorFor("session_unavailable");
            var mode = (match ?? "all").ToLowerInvariant();
            var lifecycle = (scope ?? "active").ToLowerInvariant() switch
            {
                "active" => GlobalSearchLifecycleScope.Active,
                "archived" => GlobalSearchLifecycleScope.Archived,
                _ => (GlobalSearchLifecycleScope)(-1)
            };
            // Invalid mode takes the same authenticated validation path as an
            // invalid lifecycle value; neither input bypasses actor admission.
            if (mode is not ("all" or "any")) lifecycle = (GlobalSearchLifecycleScope)(-1);
            var binding = new GlobalSearchBinding(actor.Value, (q ?? "").Trim(), (label ?? "").Trim(), (member ?? "").Trim(),
                mode == "all", lifecycle);
            var result = await service.SearchAsync(binding, after, ct);
            if (!result.Succeeded || result.Value is null) return ErrorFor(result.ErrorCode);
            var source = await interactions.SearchExecutedAsync(actor.Value, ct);
            if (!source.Succeeded || source.Value is null)
                return ErrorFor(source.ErrorCode == "identity_storage_unavailable" ? "work_storage_unavailable" : source.ErrorCode);
            return Results.Ok(result.Value with { Interaction = source.Value });
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
