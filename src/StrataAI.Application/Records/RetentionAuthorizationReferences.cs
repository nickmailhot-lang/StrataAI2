using StrataAI.Domain.Records;
using StrataAI.Application.WorkManagement;

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

public interface IRetentionSourceReferenceReader
{
    // Read only identities needed to plan admission before private classification,
    // reason or receipt bodies. Re-read after gates/locks and before disclosure.
    Task<RetentionSourceReference?> ReadSourceAsync(Guid organization, RetentionRecordClass recordClass,
        Guid record, CancellationToken ct);
}
public interface IRetentionAuthorizationReferences : IRetentionSourceReferenceReader
{
    Task<RetentionHoldReference?> ReadHoldAsync(Guid organization, Guid hold, CancellationToken ct);
    // Actor/key lookup retains the exact original history version after release.
    Task<RetentionHoldReference?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
}

public sealed record RetentionWorkSourceReference(RetentionSourceReference Classification,
    Guid CardId, Guid BoardId, Guid ListId, long CardVersion, long ListVersion,
    long SourceVersion, WorkItemLifecycleState CardState, WorkItemLifecycleState ListState,
    WorkItemLifecycleState SourceState);
public interface IRetentionWorkSourceReferences
{
    // Current relationship/version/lifecycle only; no Card, Comment or file body.
    Task<RetentionWorkSourceReference?> ReadAsync(Guid organization, RetentionRecordClass recordClass,
        Guid record, CancellationToken ct);
}
