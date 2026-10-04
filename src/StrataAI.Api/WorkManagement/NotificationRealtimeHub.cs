using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public sealed class NotificationRealtimeHub(NotificationInboxService inbox, IIdentityService identities,
    IdentityPolicy policy, ILogger<NotificationRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.NotificationRealtime.Subscription";

    public async IAsyncEnumerable<NotificationInboxService.SyncPage> Watch(Guid organizationId, string? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long? since = null;
        if (organizationId == Guid.Empty) throw new HubException("notification_not_found");
        if (cursor is not null)
        {
            if (cursor.Length is < 1 or > 19 || !cursor.All(c => c is >= '0' and <= '9') ||
                !long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw new HubException("invalid_notification_cursor");
            since = parsed;
        }
        var subscription = Guid.NewGuid();
        lock (Context.Items)
        {
            if (Context.Items.ContainsKey(SubscriptionKey)) throw new HubException("subscription_limit");
            Context.Items[SubscriptionKey] = subscription;
        }
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Context.ConnectionAborted);
        var token = stopped.Token;
        var http = Context.GetHttpContext();
        var cookie = http?.Request.Cookies[SessionAuthenticationDefaults.CookieName];
        try
        {
            var actor = await CurrentActorAsync();
            var initial = true;
            var heartbeat = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                var result = await ReadAsync(actor);
                if (!result.Succeeded || result.Value is null) Denied("notification_sync_unavailable");
                var page = result.Value!;
                if (await CurrentActorAsync() != actor || page.RecipientId != actor || page.OrganizationId != organizationId)
                    Denied("session_unavailable");
                var next = long.Parse(page.Cursor, CultureInfo.InvariantCulture);
                if (initial || page.Events.Count > 0 || next != since || page.ResetRequired || ++heartbeat >= 20)
                {
                    yield return page;
                    heartbeat = 0;
                }
                initial = false;
                since = next;
                if (!page.HasMore) await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        finally
        {
            lock (Context.Items)
                if (Context.Items.TryGetValue(SubscriptionKey, out var active) && Equals(active, subscription))
                    Context.Items.Remove(SubscriptionKey);
        }

        async Task<Guid> CurrentActorAsync()
        {
            try
            {
                var session = cookie is null ? null : await identities.AuthenticateSessionAsync(cookie, token);
                if (session is null || policy.RequireVerifiedEmail && !session.User.EmailVerified) Denied("session_unavailable");
                return session!.User.Id;
            }
            catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Notification live session unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("notification_sync_unavailable");
                throw;
            }
        }
        async Task<WorkOperation<NotificationInboxService.SyncPage>> ReadAsync(Guid actor)
        {
            try { return await inbox.ReadEventsAsync(organizationId, actor, since, token); }
            catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Notification live read unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("notification_sync_unavailable");
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
