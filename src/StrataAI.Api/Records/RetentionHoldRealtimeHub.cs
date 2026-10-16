using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using StrataAI.Application.Records;

namespace StrataAI.Api.Records;

// Replay comes from the same immutable, transactionally committed hold/audit
// journal. No transient broadcast or Worker access to private holds is required.
public sealed class RetentionHoldRealtimeHub(RetentionHoldService records, IIdentityService identities,
    IdentityPolicy policy, ILogger<RetentionHoldRealtimeHub> logger) : Hub
{
    private const string SubscriptionKey = "StrataAI.RetentionHoldRealtime.Subscription";
    public async IAsyncEnumerable<JsonElement> Watch(Guid organizationId, Guid holdId, Guid expectedActorId,
        long afterVersion, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || holdId == Guid.Empty || expectedActorId == Guid.Empty || afterVersion is < 0 or > 2)
            throw new HubException("invalid_retention_hold_cursor");
        var subscription = Guid.NewGuid();
        lock (Context.Items)
        {
            if (Context.Items.ContainsKey(SubscriptionKey)) throw new HubException("subscription_limit");
            Context.Items[SubscriptionKey] = subscription;
        }
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Context.ConnectionAborted);
        var token = stopped.Token; var http = Context.GetHttpContext();
        var cookie = http?.Request.Cookies[SessionAuthenticationDefaults.CookieName];
        var initial = true; var heartbeat = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested(); await AdmitSession();
                var result = await records.ReadSyncAsync(organizationId, expectedActorId, holdId, afterVersion, token);
                if (!result.Succeeded || result.Value is null) Denied(result.ErrorCode ?? "retention_hold_source_unavailable");
                var page = result.Value!;
                // Session IO may outlast Board or membership withdrawal. Recheck
                // actual source authority and current head after that IO.
                await AdmitSession();
                var current = await records.ReadAsync(organizationId, expectedActorId, holdId, token);
                if (!current.Succeeded || current.Value is null) Denied(current.ErrorCode ?? "retention_hold_not_found");
                if (current.Value != page.Current) continue;
                if (initial || page.Events.Count > 0 || page.ResetRequired || ++heartbeat >= 10)
                {
                    yield return JsonSerializer.SerializeToElement(page, RetentionHoldEndpoints.Options);
                    heartbeat = 0;
                }
                initial = false; afterVersion = page.AfterVersion;
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        finally
        {
            lock (Context.Items)
                if (Context.Items.TryGetValue(SubscriptionKey, out var active) && Equals(active, subscription)) Context.Items.Remove(SubscriptionKey);
        }
        async Task AdmitSession()
        {
            try
            {
                var session = cookie is null ? null : await identities.AuthenticateSessionAsync(cookie, token);
                if (session is null || session.User.Id != expectedActorId || policy.RequireVerifiedEmail && !session.User.EmailVerified)
                    Denied("session_unavailable");
            }
            catch (Exception error) when (error is not HubException && !token.IsCancellationRequested)
            {
                ApiDiagnostics.Warning(logger, "Hold live session unavailable. CorrelationId={CorrelationId}", http?.TraceIdentifier);
                Denied("retention_hold_source_unavailable");
            }
        }
    }
    private void Denied(string code) { Context.Abort(); throw new HubException(code); }
}
