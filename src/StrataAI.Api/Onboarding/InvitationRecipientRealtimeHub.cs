using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;

namespace StrataAI.Api.Onboarding;

public sealed class InvitationRecipientRealtimeHub(TransactionalInvitationRecipientSynchronization replay,
    IIdentityService identities, ILogger<InvitationRecipientRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.InvitationRecipientRealtime.Subscription";

    public async IAsyncEnumerable<InvitationRecipientSyncPage> Watch(string? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cursor is not null && cursor.Length is < 1 or > 4096) throw new HubException("invalid_invitation_cursor");
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
            var reviewed = http?.Request.Query["expectedActorId"];
            if (reviewed is { Count: > 0 } && (reviewed.Value.Count != 1
                || !Guid.TryParse(reviewed.Value[0], out var expected) || expected == Guid.Empty || expected != actor))
                Denied("session_unavailable");
            var initial = true; var heartbeat = 0; string? deliveredCheckpoint = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                var result = await ReadAsync(actor);
                if (!initial && deliveredCheckpoint is not null && !result.Succeeded && result.ErrorCode == "account_unavailable")
                {
                    var recovery = await replay.RecoverLiveReadAsync(actor, deliveredCheckpoint, token);
                    if (recovery.Succeeded && recovery.Value is { } reset)
                        result = IdentityOperation<InvitationRecipientSyncPage>.Success(reset);
                }
                if (!result.Succeeded || result.Value is null)
                    Denied(result.ErrorCode is "session_unavailable" or "account_unavailable" ? "session_unavailable" : "invitation_sync_unavailable");
                var page = result.Value!;
                if (!initial && page.ResetRequired && deliveredCheckpoint is not null)
                {
                    var recovery = await replay.RecoverLiveCheckpointAsync(actor, deliveredCheckpoint, page.Cursor, token);
                    if (!recovery.Succeeded) Denied(recovery.ErrorCode is "session_unavailable" or "account_unavailable"
                        ? "session_unavailable" : "invitation_sync_unavailable");
                    if (recovery.Value is { } recovered) page = page with { Cursor = recovered };
                }
                // Session I/O can outlive the account/email revision used by
                // replay. Rebind its protected cursor before any delivery.
                if (await CurrentActorAsync() != actor) Denied("session_unavailable");
                if (!await RequireCurrentCursorAsync(actor, page.Cursor))
                {
                    if (initial || deliveredCheckpoint is null) Denied("session_unavailable");
                    var recovery = await replay.RecoverLiveReadAsync(actor, deliveredCheckpoint!, token);
                    if (!recovery.Succeeded || recovery.Value is null) Denied("session_unavailable");
                    // Discard the selected old-binding page. Deliver an empty
                    // admitted reset, then read its source again under the new
                    // binding; never disclose a page from the invalidated read.
                    page = recovery.Value!;
                    if (await CurrentActorAsync() != actor || !await RequireCurrentCursorAsync(actor, page.Cursor))
                        Denied("session_unavailable");
                }
                if (initial || page.ResetRequired || page.Events.Count > 0 || ++heartbeat >= 20)
                {
                    yield return page;
                    deliveredCheckpoint = page.Cursor;
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
                if (session is null || session.User.Status != AccountStatus.Active || !session.User.EmailVerified)
                    Denied("session_unavailable");
                return session!.User.Id;
            }
            catch (Exception error) when (error is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Invitation recipient live session unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("invitation_sync_unavailable"); throw;
            }
        }
        async Task<IdentityOperation<InvitationRecipientSyncPage>> ReadAsync(Guid actor)
        {
            try { return await replay.ReadAsync(actor, cursor, cancellationToken: token); }
            catch (Exception error) when (error is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Invitation recipient live read unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("invitation_sync_unavailable"); throw;
            }
        }
        async Task<bool> RequireCurrentCursorAsync(Guid actor, string current)
        {
            try
            {
                var admission = await replay.IsCursorCurrentAsync(actor, current, token);
                if (!admission.Succeeded)
                    Denied(admission.ErrorCode is "session_unavailable" or "account_unavailable" ? "session_unavailable" : "invitation_sync_unavailable");
                return admission.Value;
            }
            catch (Exception error) when (error is not HubException && !token.IsCancellationRequested)
            {
                logger.LogWarning("Invitation recipient live account binding unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("invitation_sync_unavailable"); throw;
            }
        }
    }
    private void Denied(string code)
    {
        Context.Abort();
        throw new HubException(code);
    }
}
