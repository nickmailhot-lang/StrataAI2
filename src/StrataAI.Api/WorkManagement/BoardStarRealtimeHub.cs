using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public sealed record BoardStarLivePage(Guid OrganizationId, Guid BoardId, Guid UserId, string Cursor,
    IReadOnlyList<BoardStarEvent> Events, bool HasMore, bool ResetRequired);

public sealed class BoardStarRealtimeHub(IWorkManagementService work, IIdentityService identities,
    IdentityPolicy policy, ILogger<BoardStarRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.BoardStarRealtime.Subscription";
    public async IAsyncEnumerable<BoardStarLivePage> Watch(Guid boardId, string? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscription = Guid.NewGuid();
        lock (Context.Items)
        {
            if (Context.Items.ContainsKey(SubscriptionKey)) throw new HubException("subscription_limit");
            Context.Items[SubscriptionKey] = subscription;
        }
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,Context.ConnectionAborted);
        var token = stopped.Token; var http = Context.GetHttpContext();
        var cookie = http?.Request.Cookies[SessionAuthenticationDefaults.CookieName];
        try
        {
            var actor = await CurrentActorAsync(); long? since = null;
            var invalid = cursor is not null && (cursor.Length is < 1 or > 19 || !cursor.All(c => c is >= '0' and <= '9')
                || !long.TryParse(cursor,NumberStyles.None,CultureInfo.InvariantCulture,out _));
            if (cursor is not null && !invalid) since = long.Parse(cursor,CultureInfo.InvariantCulture);
            var initial = true; var heartbeat = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                var head = await HeadAsync(actor);
                if (invalid) Denied("invalid_board_star_cursor");
                var reset = since > head.Version;
                since = reset ? 0 : since ?? head.Version;
                var page = await EventsAsync(actor,since.Value);
                // Re-admit Board scope after disclosure selection and account
                // proof before yielding. Never advance using this newer head.
                var current = await HeadAsync(actor);
                if (current.OrganizationId != page.OrganizationId || current.UserId != actor ||
                    await CurrentActorAsync() != actor) Denied("session_unavailable");
                var next = page.NextAfter ?? Math.Max(head.Version,page.Items.LastOrDefault()?.Version ?? since.Value);
                if (initial || page.Items.Count > 0 || reset || next != since || ++heartbeat >= 20)
                {
                    yield return new(page.OrganizationId,boardId,actor,next.ToString(CultureInfo.InvariantCulture),
                        page.Items,page.NextAfter.HasValue,reset);
                    heartbeat = 0;
                }
                initial = false; since = next;
                if (!page.NextAfter.HasValue) await Task.Delay(TimeSpan.FromSeconds(1),token);
            }
        }
        finally
        {
            lock (Context.Items)
                if (Context.Items.TryGetValue(SubscriptionKey,out var active) && Equals(active,subscription))
                    Context.Items.Remove(SubscriptionKey);
        }
        async Task<Guid> CurrentActorAsync()
        {
            try
            {
                var session = cookie is null ? null : await identities.AuthenticateSessionAsync(cookie,token);
                if (session is null || policy.RequireVerifiedEmail && !session.User.EmailVerified) Denied("session_unavailable");
                return session!.User.Id;
            }
            catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Private star session unavailable. CorrelationId={CorrelationId}",http?.TraceIdentifier);
                Denied("board_star_sync_unavailable"); throw;
            }
        }
        async Task<BoardStarPreference> HeadAsync(Guid actor)
        {
            try
            {
                var result = await work.GetStarAsync(boardId,actor,token);
                if (!result.Succeeded || result.Value is null) Denied("board_not_found");
                return result.Value!;
            }
            catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Private star admission unavailable. CorrelationId={CorrelationId}",http?.TraceIdentifier);
                Denied("board_star_sync_unavailable"); throw;
            }
        }
        async Task<BoardStarEventPage> EventsAsync(Guid actor,long after)
        {
            try
            {
                var result = await work.GetStarEventsAsync(boardId,actor,after,token);
                if (!result.Succeeded || result.Value is null || result.Value.UserId != actor || result.Value.BoardId != boardId)
                    Denied("board_not_found");
                return result.Value!;
            }
            catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Private star delivery unavailable. CorrelationId={CorrelationId}",http?.TraceIdentifier);
                Denied("board_star_sync_unavailable"); throw;
            }
        }
    }
    private void Denied(string code) { Context.Abort(); throw new HubException(code); }
}
