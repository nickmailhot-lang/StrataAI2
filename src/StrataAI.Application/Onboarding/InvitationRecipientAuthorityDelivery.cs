using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.Onboarding;

public interface IInvitationRecipientAuthorityDeliveryStore
{
    // One leased transaction must verify the original canonical source and
    // private page checkpoint, scan at most candidateLimit rows, deduplicate
    // recipient effects, and persist any next-page job before returning true.
    // No recipient addresses or parent metadata cross this capability.
    Task<bool> DeliverNextPageAsync(ClaimedBackgroundJob job, Guid sourceEventId, int candidateLimit,
        CancellationToken cancellationToken);
}

public sealed class InvitationRecipientAuthorityDeliveryHandler(IInvitationRecipientAuthorityDeliveryStore store)
    : IBackgroundJobHandler
{
    public const string Type = "INVITATION_RECIPIENT_AUTHORITY_PAGE";
    public const string Service = "invitation-recipient-authority";
    public const int CandidateLimit = 100;
    public string JobType => Type;
    public string ServiceIdentity => Service;

    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (job.JobType != Type || job.ServiceIdentity != Service || job.OrganizationId == Guid.Empty
            || job.ActorId == Guid.Empty || job.Id == Guid.Empty || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty)
            throw new InvalidOperationException("Invitation recipient authority delivery scope is invalid.");
        var source = OrganizationLifecycleDeliveryHandler.ParseEventId(job.SafeMetadataJson);
        if (!await store.DeliverNextPageAsync(job, source, CandidateLimit, cancellationToken))
            throw new InvalidOperationException("Invitation recipient authority source or lease is unavailable.");
        // Never acknowledge a cancellation observed after storage returns.
        // Committed effects/continuations must survive replay of the old lease.
        cancellationToken.ThrowIfCancellationRequested();
    }
}
