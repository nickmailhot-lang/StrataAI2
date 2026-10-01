using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.Onboarding;

public sealed class InvitationEmailHandler(IInvitationDeliveryStore store, IInvitationDeliveryTokenSigner signer,
    IIdentityEmailProvider provider, IdentityDeliveryOptions options, IClock clock) : IBackgroundJobHandler
{
    public const string Type = "INVITATION_EMAIL";
    public const string Identity = "invitation-email-delivery";
    public string JobType => Type;
    public string ServiceIdentity => Identity;

    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        var invitationId = ReadReference(job.SafeMetadataJson);
        if (job.JobType != Type || job.ServiceIdentity != Identity || job.OrganizationId == Guid.Empty
            || job.Id == Guid.Empty || job.ActorId == Guid.Empty || job.WorkerId == Guid.Empty || job.LeaseId == Guid.Empty)
            throw new InvalidOperationException("Invalid invitation delivery claim.");
        var intent = await store.LoadAsync(job, cancellationToken);
        if (intent is null || intent.JobId != job.Id || intent.OrganizationId != job.OrganizationId
            || intent.InvitationId != invitationId || intent.IssuerId != job.ActorId)
            throw new InvalidOperationException("Invitation delivery claim was not admitted.");
        // Historical delivery completion is not a new send or recipient access grant.
        if (intent.State is InvitationMailState.Sent or InvitationMailState.Cancelled or InvitationMailState.Failed) return;
        if (intent.State != InvitationMailState.Pending) throw new InvalidOperationException("Invalid invitation delivery state.");
        if (!intent.IsUsable || intent.ExpiresAt <= clock.UtcNow)
        {
            await FinishAsync(InvitationMailState.Cancelled, "invitation_unusable", null); return;
        }
        if (!ValidIntent(intent))
        {
            await FinishAsync(InvitationMailState.Failed, "invitation_intent_invalid", null); return;
        }
        if (intent.ProviderAccount != options.ProviderAccount)
        {
            await FinishAsync(InvitationMailState.Failed, "invitation_provider_account_changed", null); return;
        }
        string token;
        try { token = signer.DeriveInvitation(intent.OrganizationId, intent.InvitationId, intent.KeyId); }
        catch (InvalidOperationException)
        {
            await FinishAsync(InvitationMailState.Failed, "invitation_token_key_missing", null); return;
        }
        var calculated = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (intent.CanonicalTokenHash is not { Length: 64 } hash
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(calculated)))
        {
            await FinishAsync(InvitationMailState.Failed, "invitation_token_hash_mismatch", null); return;
        }
        var deadline = (job.LeaseExpiresAt < intent.ExpiresAt ? job.LeaseExpiresAt : intent.ExpiresAt) - clock.UtcNow - TimeSpan.FromSeconds(5);
        if (deadline <= TimeSpan.Zero) throw new InvalidOperationException("Invitation delivery has insufficient lease time.");
        // Template 1 is immutable across retries/deployments; protected names and
        // roles are reviewed on the authorized recipient screen, not emailed.
        var message = new IdentityEmailMessage(intent.SenderAddress, intent.RecipientEmail, "Your StrataAI2 invitation",
            $"An administrator has invited you to StrataAI2.\n\nReview your invitation:\n{intent.PublicOrigin}/invitation#token={token}\n\nSign in with this invited email or create an account, verify your email if asked, and review the access before accepting.\n\nThis link expires. If you were not expecting this invitation, ignore this email.",
            $"strataai-invitation/{intent.OrganizationId:N}/{intent.InvitationId:N}");
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        execution.CancelAfter(deadline);
        Guid receipt;
        try { receipt = await provider.SendAsync(message, execution.Token).WaitAsync(execution.Token); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (IdentityEmailProviderException error) when (error.Permanent)
        {
            await FinishAsync(InvitationMailState.Failed, "invitation_provider_rejected", null); return;
        }
        catch (Exception)
        {
            // Do not persist/log provider text, credentials, recipient or bearer.
            // Generic background-job retry owns bounded attempts and crash recovery.
            throw new InvalidOperationException("Invitation provider did not acknowledge delivery.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (receipt == Guid.Empty) throw new InvalidOperationException("Invitation provider receipt was not valid.");
        await FinishAsync(InvitationMailState.Sent, null, receipt);

        async Task FinishAsync(InvitationMailState state, string? code, Guid? providerReceipt)
        {
            if (!await store.FinishAsync(job, state, code, providerReceipt, cancellationToken))
                throw new InvalidOperationException("Invitation delivery lease was not acknowledged.");
        }
    }

    private static Guid ReadReference(string metadata)
    {
        using var document = JsonDocument.Parse(metadata);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Invalid invitation job reference.");
        var fields = document.RootElement.EnumerateObject().ToArray();
        if (fields.Length != 1 || fields[0].Name != "invitationId" || fields[0].Value.ValueKind != JsonValueKind.String
            || !fields[0].Value.TryGetGuid(out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Invalid invitation job reference.");
        return id;
    }

    private static bool ValidIntent(InvitationMailIntent intent) => intent.TemplateVersion == 1
        && PlainAddress(intent.RecipientEmail) && PlainAddress(intent.SenderAddress)
        && (intent.Surface == InvitationSurface.Internal ? intent.TargetRole is "OWNER" or "ADMIN" or "MEMBER"
            : intent.Surface == InvitationSurface.Portal && intent.TargetRole is "OWNER" or "CO_OWNER" or "TENANT" or "OCCUPANT" or "AUTHORIZED_REPRESENTATIVE" or "OTHER")
        && Uri.TryCreate(intent.PublicOrigin, UriKind.Absolute, out var origin) && origin.Scheme == "https"
        && origin.AbsolutePath == "/" && origin.Query == "" && origin.Fragment == "" && origin.UserInfo == "";
    private static bool PlainAddress(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 320
        && MailAddress.TryCreate(value, out var address) && address.Address == value && address.DisplayName == "";
}
