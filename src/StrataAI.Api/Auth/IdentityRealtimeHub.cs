using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Application.Identity;

namespace StrataAI.Api.Auth;

public sealed class IdentityRealtimeHub(IIdentityService identities, IdentityPolicy policy,
    ILogger<IdentityRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.IdentityRealtime.Subscription";

    public async IAsyncEnumerable<IdentitySyncSnapshot> Watch(string? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long? since = null;
        if (cursor is not null)
        {
            if (cursor.Length is < 1 or > 19 || !cursor.All(c => c is >= '0' and <= '9')
                || !long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw new HubException("invalid_identity_cursor");
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
                if (!result.Succeeded || result.Value is null)
                    Denied(result.ErrorCode == "session_unavailable" ? "session_unavailable" : "identity_sync_unavailable");
                var page = result.Value!;
                // A successful handshake never grants an indefinitely valid account stream.
                if (await CurrentActorAsync() != actor || page.Profile.Id != actor) Denied("session_unavailable");
                if (initial || page.Events.Count > 0 || ++heartbeat >= 20)
                {
                    yield return page;
                    heartbeat = 0;
                }
                initial = false;
                since = page.Cursor;
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
                logger.LogWarning("Identity live session check unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("identity_sync_unavailable");
                throw;
            }
        }
        async Task<IdentityOperation<IdentitySyncSnapshot>> ReadAsync(Guid actor)
        {
            try { return await identities.ReadEventsAsync(actor, since, token); }
            catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Identity live read unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("identity_sync_unavailable");
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
