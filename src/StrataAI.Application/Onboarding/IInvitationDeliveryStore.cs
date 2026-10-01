using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.Onboarding;

public enum InvitationMailState { Pending, Sent, Cancelled, Failed }

// Protected canonical/snapshot material. Only invitationId belongs in job metadata.
public sealed record InvitationMailIntent(Guid JobId, Guid OrganizationId, Guid InvitationId, Guid IssuerId,
    string RecipientEmail, InvitationSurface Surface, string TargetRole, DateTimeOffset ExpiresAt,
    string KeyId, string SenderAddress, string PublicOrigin, string ProviderAccount, int TemplateVersion,
    InvitationMailState State, string? CanonicalTokenHash, bool IsUsable);

public interface IInvitationDeliveryStore
{
    // Must bind the live tenant/job/worker/lease/service claim and recheck canonical
    // invitation, recipient, issuer/grant, lifecycle and expiry in a short transaction.
    // No connection/lock is retained across provider work.
    Task<InvitationMailIntent?> LoadAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken);
    // Terminal ledger acknowledgment is conditional on the same still-live lease.
    // Sent persists before generic job completion, allowing crash-safe acknowledgment recovery.
    Task<bool> FinishAsync(ClaimedBackgroundJob job, InvitationMailState state, string? safeErrorCode,
        Guid? providerReceiptId, CancellationToken cancellationToken);
}
