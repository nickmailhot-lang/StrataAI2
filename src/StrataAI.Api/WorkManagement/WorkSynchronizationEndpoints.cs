using System.Globalization;
using System.Security.Claims;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static class WorkSynchronizationEndpoints
{
    public static void MapWorkSynchronizationEndpoints(this WebApplication app)
    {
        app.MapGet("/boards/{boardId:guid}/sync", async (Guid boardId, HttpContext context,
            WorkSynchronizationService service, ILogger<WorkSynchronizationService> logger, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!Number(context, "since", 0, out var since)) return Problem(400, "invalid_sync_cursor");
            if (!Number(context, "limit", 100, out var limit) || limit is < 1 or > 100)
                return Problem(400, "invalid_sync_limit");
            Guid? actor = Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
            try
            {
                var result = await service.ReadAsync(boardId, actor, since, (int)limit, cancellationToken);
                return result.Succeeded && result.Value is not null ? Results.Ok(result.Value)
                    : Problem(result.ErrorCode == "work_sync_unavailable" ? 503 : 404, result.ErrorCode ?? "board_not_found");
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                exception is not (RuntimeDatabaseRoleException or RuntimeDatabaseSchemaException))
            {
                // No exception body or event details: SQL diagnostics can contain
                // protected data. This read never discloses a partial event batch.
                logger.LogWarning("Work synchronization read unavailable. CorrelationId={CorrelationId}", context.TraceIdentifier);
                return Problem(503, "work_sync_unavailable");
            }
        });
    }

    private static bool Number(HttpContext context, string name, long fallback, out long value)
    {
        value = fallback;
        if (!context.Request.Query.TryGetValue(name, out var values)) return true;
        var text = values.Count == 1 ? values[0] : null;
        return text is { Length: > 0 and <= 19 } &&
            text.All(character => character is >= '0' and <= '9') &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        title: status == 503 ? "Synchronization is temporarily unavailable." : status == 400
            ? "A valid synchronization cursor and page size are required." : "The Board was not found.",
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
