using Microsoft.AspNetCore.Http.Features;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapNavigationInteractionEndpoints(WebApplication app)
    {
        app.MapPost("/navigation/observations", async (string? kind, Guid? organizationId, Guid? boardId,
            Guid? cardId, long? version, HttpContext context, IWorkCommandContext commands,
            NavigationInteractionReplayProducer producer, CancellationToken ct) => {
            context.Response.Headers.CacheControl = "private, no-store";
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            if (!context.Request.Headers.TryGetValue("X-StrataAI-Expected-Actor", out var expected) || expected.Count != 1
                || expected[0]?.Length != 36 || !Guid.TryParseExact(expected[0], "D", out var expectedActor)
                || expectedActor == Guid.Empty) return ErrorFor("invalid_navigation");
            if (expectedActor != actor.Value) return ErrorFor("session_unavailable");
            if (context.Request.Query.Any(q => q.Value.Count != 1 || q.Key is not ("kind" or "organizationId" or "boardId" or "cardId" or "version"))
                || context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                || context.Request.ContentLength is > 0 || context.Request.Headers.TransferEncoding.Count > 0
                || organizationId == Guid.Empty || boardId == Guid.Empty || cardId == Guid.Empty
                || kind is not ("context" or "board" or "card")) return ErrorFor("invalid_navigation");
            if (kind == "context" && (boardId.HasValue || cardId.HasValue || version.HasValue)
                || kind == "board" && (!organizationId.HasValue || !boardId.HasValue || cardId.HasValue || version is not > 0)
                || kind == "card" && (!organizationId.HasValue || !boardId.HasValue || !cardId.HasValue || version is not > 0))
                return ErrorFor("invalid_navigation");
            if (commands.IdempotencyKey is not { } key) return ErrorFor("invalid_idempotency_key");
            var digest = WorkCommand.Create(actor.Value, key, "NavigationObservation", cardId ?? boardId ?? organizationId ?? Guid.Empty,
                new { kind, organizationId, boardId, cardId, version }, "navigation_unavailable").Fingerprint.ToLowerInvariant();
            var result = kind switch {
                "context" => await producer.ContextAsync(actor.Value, organizationId, key, digest, ct),
                "board" => await producer.BoardAsync(actor.Value, organizationId!.Value, boardId!.Value, version!.Value, key, digest, ct),
                _ => await producer.CardAsync(actor.Value, organizationId!.Value, boardId!.Value, cardId!.Value, version!.Value, key, digest, ct)
            };
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization();
    }
}
