using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Api.Organizations;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Api.WorkManagement;

public sealed record OrganizationOperationalLifecycleLivePage(Guid OrganizationId, Guid UserId, OrganizationOperationalLifecycleWirePage Page);
public sealed class OrganizationOperationalLifecycleRealtimeHub(OrganizationOperationalLifecycleReplayService replay,
    IIdentityService identities, IdentityPolicy policy, ILogger<OrganizationOperationalLifecycleRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.OrganizationOperationalLifecycleRealtime.Subscription";
    public async IAsyncEnumerable<OrganizationOperationalLifecycleLivePage> Watch(Guid organizationId, long afterVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || afterVersion < 0) throw new HubException("invalid_organization_cursor");
        var subscription = Guid.NewGuid();
        lock (Context.Items)
        {
            if (Context.Items.ContainsKey(SubscriptionKey)) throw new HubException("subscription_limit");
            Context.Items[SubscriptionKey] = subscription;
        }
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Context.ConnectionAborted);
        var token = stopped.Token; var http = Context.GetHttpContext();
        var cookie = http?.Request.Cookies[SessionAuthenticationDefaults.CookieName];
        try
        {
            var actor = await CurrentActorAsync(); var initial = true; var heartbeat = 0;
            OrganizationStatus? previousStatus = null; long? previousVersion = null; bool? previousPending = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                var page = await ReadAsync(actor);
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                // Discard the earlier page: session IO may outlast membership
                // withdrawal or another parent revision. Re-enter owning admission.
                page = await ReadAsync(actor);
                token.ThrowIfCancellationRequested();
                if (initial || page.Events.Count > 0 || page.Status != previousStatus || page.Version != previousVersion
                    || page.Pending != previousPending || ++heartbeat >= 20)
                {
                    yield return new(organizationId, actor, OrganizationOperationalLifecycleTransport.Page(page));
                    heartbeat = 0; previousStatus = page.Status; previousVersion = page.Version; previousPending = page.Pending;
                }
                initial = false;
                if (page.Events.Count > 0) afterVersion = page.Events[^1].Version;
                if (page.NextVersion is null || page.Pending) await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        finally
        {
            lock (Context.Items)
                if (Context.Items.TryGetValue(SubscriptionKey, out var active) && Equals(active, subscription)) Context.Items.Remove(SubscriptionKey);
        }
        async Task<OrganizationOperationalLifecycleReplayPage> ReadAsync(Guid actor)
        {
            try
            {
                var result = await replay.ReadAsync(organizationId, actor, afterVersion, token);
                if (!result.Succeeded || result.Value is null) Denied(result.ErrorCode ?? "organization_storage_unavailable");
                return result.Value!;
            }
            catch (Exception error) when (error is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Operational lifecycle stream unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("organization_storage_unavailable"); throw;
            }
        }
        async Task<Guid> CurrentActorAsync()
        {
            try
            {
                var session = cookie is null ? null : await identities.AuthenticateSessionAsync(cookie, token);
                if (session is null || policy.RequireVerifiedEmail && !session.User.EmailVerified) Denied("session_unavailable");
                return session!.User.Id;
            }
            catch (Exception error) when (error is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Operational lifecycle session unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("organization_storage_unavailable"); throw;
            }
        }
    }
    private void Denied(string code) { Context.Abort(); throw new HubException(code); }
}
