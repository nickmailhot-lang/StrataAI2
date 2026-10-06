using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.Organizations;

public enum OrganizationDeletionPhase { Attachments, Cards, Lists, Boards, Finalize }
public sealed record OrganizationDeletionCandidate(Guid Id, Guid BoardId, Guid? CardId, long Version, string State);
public sealed record OrganizationDeletionCandidatePage(OrganizationDeletionPhase Phase, Guid? AfterId, long Version,
    IReadOnlyList<OrganizationDeletionCandidate> Items, Guid? NextCursor);
public interface IOrganizationDeletionCandidateReader
{
    // Read-only reference snapshot. Mutation must independently revalidate the
    // owning checkpoint, current row versions and lease in its transaction.
    Task<OrganizationDeletionCandidatePage?> ReadAsync(ClaimedBackgroundJob job, OrganizationDeletionAttempt attempt,
        int pageSize, CancellationToken cancellationToken);
}
