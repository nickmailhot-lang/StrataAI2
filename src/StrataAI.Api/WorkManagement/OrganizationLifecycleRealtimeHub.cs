using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Api.WorkManagement;

public sealed record OrganizationLifecycleLivePage(Guid OrganizationId, Guid UserId, OrganizationLifecyclePage Page);
public sealed class OrganizationLifecycleRealtimeHub(IOrganizationLifecycleEventReader reader,
    IIdentityService identities, IdentityPolicy policy, ILogger<OrganizationLifecycleRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.OrganizationLifecycleRealtime.Subscription";
    public async IAsyncEnumerable<OrganizationLifecycleLivePage> Watch(Guid organizationId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty) throw new HubException("organization_not_found");
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
            var actor = await CurrentActorAsync(); string? previousState = null; Guid? previousEvent = null; var heartbeat = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                var page = await ReadAsync(actor);
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                // Session I/O can outlast scope withdrawal. Use another owning
                // admission/read after it, rather than delivering the old page.
                page = await ReadAsync(actor);
                var source = page.Events.Count == 1 ? page.Events[0].EventId : (Guid?)null;
                if (previousState != page.State || previousEvent != source || ++heartbeat >= 20)
                {
                    yield return new(organizationId, actor, page);
                    heartbeat = 0; previousState = page.State; previousEvent = source;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        finally
        {
            lock (Context.Items)
                if (Context.Items.TryGetValue(SubscriptionKey, out var active) && Equals(active, subscription))
                    Context.Items.Remove(SubscriptionKey);
        }
        async Task<OrganizationLifecyclePage> ReadAsync(Guid actor)
        {
            try
            {
                var result = await reader.ReadAsync(organizationId, actor, token);
                if (!result.Succeeded || result.Value is null) Denied(result.ErrorCode ?? "organization_storage_unavailable");
                return result.Value!;
            }
            catch (Exception error) when (error is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Organization lifecycle stream unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
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
                logger.LogWarning("Organization lifecycle session unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("organization_storage_unavailable"); throw;
            }
        }
    }
    private void Denied(string code) { Context.Abort(); throw new HubException(code); }
}
