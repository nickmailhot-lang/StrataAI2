using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public sealed class WorkRealtimeOrigin
{
    public string? Authority { get; }
    public WorkRealtimeOrigin(IConfiguration configuration)
    {
        var value = configuration["STRATAAI_REALTIME_PUBLIC_ORIGIN"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["STRATAAI_PUBLIC_ORIGIN"];
        if (string.IsNullOrWhiteSpace(value)) return;
        Authority = Normalize(value) ?? throw new InvalidOperationException("A valid Work realtime public origin is required.");
    }
    public bool Allows(string? value) => Authority is not null &&
        string.Equals(Authority, Normalize(value), StringComparison.OrdinalIgnoreCase);
    private static string? Normalize(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0
        ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) : null;
}

public sealed class WorkRealtimeOriginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, WorkRealtimeOrigin origin, ILogger<WorkRealtimeOriginMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments("/boards/live") && !context.Request.Path.StartsWithSegments("/me/live") && !context.Request.Path.StartsWithSegments("/notifications/live") && !context.Request.Path.StartsWithSegments("/organizations/live") && !context.Request.Path.StartsWithSegments("/invitations/live")) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        var values = context.Request.Headers.Origin;
        var enabled = origin.Authority is not null;
        if (!enabled || values.Count != 1 || !origin.Allows(values[0]))
        {
            await Results.Problem(statusCode: enabled ? 403 : 503, title: "Live updates are unavailable.",
                extensions: new Dictionary<string, object?> { ["code"] = enabled ? "realtime_origin_denied" : "realtime_unavailable" }).ExecuteAsync(context);
            return;
        }
        try { await next(context); }
        catch (Exception exception) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested &&
            exception is not (RuntimeDatabaseRoleException or RuntimeDatabaseSchemaException))
        {
            logger.LogWarning("Work live connection unavailable. CorrelationId={CorrelationId}", context.TraceIdentifier);
            await Results.Problem(statusCode: 503, title: "Live updates are unavailable.",
                extensions: new Dictionary<string, object?> { ["code"] = "realtime_unavailable" }).ExecuteAsync(context);
        }
    }
}

public sealed class WorkRealtimeHub(WorkSynchronizationService synchronization, IWorkBoardAuthorization authorization,
    IIdentityService identities, IdentityPolicy policy, ILogger<WorkRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.WorkRealtime.Subscription";

    public async IAsyncEnumerable<BoardSyncPage> Watch(Guid boardId, string cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cursor is not { Length: > 0 and <= 19 } || !cursor.All(c => c is >= '0' and <= '9') ||
            !long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var since))
            throw new HubException("invalid_sync_cursor");
        var subscription = Guid.NewGuid();
        lock (Context.Items)
        {
            if (Context.Items.ContainsKey(SubscriptionKey)) throw new HubException("subscription_limit");
            Context.Items[SubscriptionKey] = subscription;
        }
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Context.ConnectionAborted);
        var token = stopped.Token;
        var cookie = Context.GetHttpContext()?.Request.Cookies[SessionAuthenticationDefaults.CookieName];
        var correlation = Context.GetHttpContext()?.TraceIdentifier;
        try
        {
            var actor = await CurrentActorAsync(cookie, token);
            var initial = true;
            bool? previousPending = null;
            var heartbeat = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await CurrentActorAsync(cookie, token) != actor) Denied("session_unavailable");
                var scope = await SafeAsync(() => authorization.GetSyncScopeAsync(boardId, actor, token), token);
                if (!scope.Succeeded || scope.Value is null) Denied("board_unavailable");
                var page = await ReadAsync(boardId, actor, since, token);
                // Live connections do not inherit an indefinitely valid handshake
                // principal. Revalidate session and permissions after awaited IO.
                if (await CurrentActorAsync(cookie, token) != actor) Denied("session_unavailable");
                var current = await SafeAsync(() => authorization.GetSyncScopeAsync(boardId, actor, token), token);
                if (!current.Succeeded || current.Value is null) Denied("board_unavailable");
                if (current.Value!.Board.OrganizationId != scope.Value!.Board.OrganizationId ||
                    current.Value.Access != scope.Value.Access || current.Value.Board.Version != scope.Value.Board.Version)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                    continue; // Re-read/redact under the changed permission revision.
                }
                if (initial || page.Events.Count > 0 || page.ResetRequired || previousPending != page.Pending || ++heartbeat >= 20)
                {
                    yield return page;
                    heartbeat = 0;
                }
                initial = false;
                previousPending = page.Pending;
                since = long.Parse(page.Cursor, CultureInfo.InvariantCulture);
                if (!page.HasMore) await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        finally
        {
            lock (Context.Items)
                if (Context.Items.TryGetValue(SubscriptionKey, out var active) && Equals(active, subscription))
                    Context.Items.Remove(SubscriptionKey);
        }

        async Task<Guid?> CurrentActorAsync(string? rawCookie, CancellationToken ct)
        {
            if (rawCookie is null) return null;
            var session = await SafeAsync(() => identities.AuthenticateSessionAsync(rawCookie, ct), ct);
            if (session is null || policy.RequireVerifiedEmail && !session.User.EmailVerified) Denied("session_unavailable");
            return session!.User.Id;
        }
        async Task<BoardSyncPage> ReadAsync(Guid board, Guid? actorId, long after, CancellationToken ct)
        {
            var result = await SafeAsync(() => synchronization.ReadAsync(board, actorId, after, cancellationToken: ct), ct);
            if (!result.Succeeded || result.Value is null)
                Denied(result.ErrorCode == "board_not_found" ? "board_unavailable" : "sync_unavailable");
            return result.Value!;
        }
        async Task<T> SafeAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            try { return await operation(); }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Work live read unavailable. CorrelationId={CorrelationId}", correlation);
                Denied("sync_unavailable");
                throw;
            }
        }
    }

    private void Denied(string code)
    {
        Context.Abort();
        throw new HubException(code);
    }
}
