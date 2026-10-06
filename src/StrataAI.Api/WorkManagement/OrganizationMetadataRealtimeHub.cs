using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using StrataAI.Application.Organizations;

namespace StrataAI.Api.WorkManagement;

public sealed record OrganizationMetadataLivePage(Guid OrganizationId, Guid UserId, OrganizationMetadataSyncPage Page);
public sealed class OrganizationMetadataRealtimeHub(TransactionalOrganizationMetadataSynchronization replay,
    IIdentityService identities, IdentityPolicy policy, ILogger<OrganizationMetadataRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.OrganizationMetadataRealtime.Subscription";
    public IAsyncEnumerable<OrganizationMetadataLivePage> Watch(Guid organizationId, string? cursor,
        CancellationToken cancellationToken) => WatchCore(replay, organizationId, cursor, cancellationToken);
    private async IAsyncEnumerable<OrganizationMetadataLivePage> WatchCore(TransactionalOrganizationMetadataSynchronization selected,
        Guid organizationId, string? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || cursor is { Length: > 4096 }) throw new HubException("invalid_sync_cursor");
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
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                WorkOperation<OrganizationMetadataSyncPage> result;
                try { result = await selected.ReadAsync(organizationId, actor, cursor, cancellationToken: token); }
                catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
                {
                    logger.LogWarning("Organization live replay unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                    Denied("organization_sync_unavailable"); throw;
                }
                if (!result.Succeeded || result.Value is null) Denied(result.ErrorCode ?? "organization_sync_unavailable");
                var page = result.Value!;
                // Session IO can outlast a permission withdrawal. Check the
                // delivered cursor binding against current scope after that IO.
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                WorkOperation<bool> current;
                try { current = await selected.IsCursorCurrentAsync(organizationId, actor, page.Cursor, token); }
                catch (Exception exception) when (exception is not HubException && !token.IsCancellationRequested)
                {
                    logger.LogWarning("Organization live admission unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                    Denied("organization_sync_unavailable"); throw;
                }
                if (!current.Succeeded || !current.Value) Denied(current.ErrorCode ?? "organization_sync_unavailable");
                if (initial || page.Events.Count > 0 || page.ResetRequired || ++heartbeat >= 20)
                {
                    yield return new(organizationId, actor, page);
                    heartbeat = 0;
                }
                initial = false; cursor = page.Cursor;
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
                logger.LogWarning("Organization live session unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("organization_sync_unavailable"); throw;
            }
        }
    }
    private void Denied(string code) { Context.Abort(); throw new HubException(code); }
}
