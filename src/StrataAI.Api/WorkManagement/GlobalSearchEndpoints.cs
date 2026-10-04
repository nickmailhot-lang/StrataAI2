using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapGlobalSearchEndpoints(WebApplication app)
    {
        app.MapGet("/search", async (string? q, string? label, string? member, string? match, string? scope, string? after,
            HttpContext context, GlobalSearchService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
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
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
