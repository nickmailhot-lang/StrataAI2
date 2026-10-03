namespace StrataAI.Application.WorkManagement;

public sealed record AttachmentScanRecoveryResult(int Visited,int Recovered);

public interface IAttachmentScanRecoveryStore
{
    // Bounded Worker-only metadata completion of exhausted quarantine jobs.
    Task<AttachmentScanRecoveryResult> RecoverPageAsync(Guid organizationId,int maximumRows,CancellationToken cancellationToken);
}
