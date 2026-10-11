using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Private owning-transaction hints, never HTTP projections or authorization.
// Missing historical evidence remains explicit; a current Board cannot replace it.
public enum RetentionProducerKind { Card, CardComment, Attachment, AuditEvent }
public sealed record RetentionSourceReference(Guid OrganizationId, RetentionRecordClass RecordClass,
    Guid RecordId, RetentionProducerKind Kind, Guid? ParentCardId,
    short? ContextVersion, Guid? OriginalBoardId, long? SourceVersion);
public sealed record RetentionHoldReference(Guid OrganizationId, Guid HoldId, long Version,
    RetentionRecordClass? RecordClass, bool CategoryScoped, Guid? RecordId,
    short? ContextVersion, Guid? ClassificationBoardId, Guid? ApplicationBoardId,
    long? ApplicationSourceVersion);

public interface IRetentionAuthorizationReferences
{
    // Read only identities needed to plan admission before private classification,
    // reason or receipt bodies. Re-read after gates/locks and before disclosure.
    Task<RetentionSourceReference?> ReadSourceAsync(Guid organization, RetentionRecordClass recordClass,
        Guid record, CancellationToken ct);
    Task<RetentionHoldReference?> ReadHoldAsync(Guid organization, Guid hold, CancellationToken ct);
    // Actor/key lookup retains the exact original history version after release.
    Task<RetentionHoldReference?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
}
