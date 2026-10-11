using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Records;

internal sealed record DemoRetentionSourceWrite(Guid OrganizationId, RetentionRecordClass RecordClass,
    Guid RecordId, RetentionProducerKind Kind, string Category, string? Jurisdiction, long SourceVersion,
    long CardVersion, Guid ActorId, Guid MembershipId, long MembershipVersion, OrganizationRole Capacity,
    Guid EventId, string CorrelationId, DateTimeOffset RegisteredAt);
internal sealed record DemoRetentionSourceClassification(RetentionSourceReference Reference, DemoRetentionSourceWrite Source, string ActorLabel);

// Unregistered storage primitive. Human review, exact retry and Production command
// parity remain required; no HTTP seed or guessed historical source is accepted.
internal sealed class InMemoryRetentionSourceStore(IOrganizationStore organizations, IIdentityStore identities,
    ICommandActorAuthorization actors, IWorkManagementStore work, IWorkBoardAuthorization boards,
    DemoWorkTransactionScope scope, IClock clock) : IRetentionSourceReferenceReader, IRetentionWorkSourceReferences,
    IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid Organization, RetentionRecordClass Class, Guid Record), DemoRetentionSourceClassification> rows = [];
    public Task<RetentionSourceReference?> ReadSourceAsync(Guid organization, RetentionRecordClass recordClass, Guid record, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(rows.GetValueOrDefault((organization, recordClass, record))?.Reference);
    }
    public async Task<RetentionWorkSourceReference?> ReadAsync(Guid organization, RetentionRecordClass recordClass, Guid record, CancellationToken ct)
    {
        var reference = await ReadSourceAsync(organization, recordClass, record, ct);
        if (reference is null) return null;
        var current = Producer(organization, reference.Kind, record, ct);
        return current is null ? null : new(reference, current.CardId, current.BoardId, current.ListId,
            current.CardVersion, current.ListVersion, current.SourceVersion, current.CardState, current.ListState, current.SourceState);
    }
    internal async Task<RetentionSourceReference?> CaptureAsync(DemoRetentionSourceWrite write, CancellationToken ct)
    {
        RequireScope(write.OrganizationId, ct);
        var key = (write.OrganizationId, write.RecordClass, write.RecordId);
        var expectedClass = write.Kind == RetentionProducerKind.Attachment ? RetentionRecordClass.Document : RetentionRecordClass.OperationalWork;
        if (write.Kind is not (RetentionProducerKind.Card or RetentionProducerKind.CardComment or RetentionProducerKind.Attachment)
            || write.RecordClass != expectedClass || rows.ContainsKey(key) || write.RecordId == Guid.Empty
            || RecordRetentionRules.Validate(new RetentionPolicyApplicability(write.OrganizationId, write.RecordClass, write.Category, write.Jurisdiction)) is not null
            || write.EventId == Guid.Empty || string.IsNullOrWhiteSpace(write.CorrelationId) || write.CorrelationId.Length > 256 || write.CorrelationId.Any(char.IsControl)
            || write.RegisteredAt.Offset != TimeSpan.Zero || write.RegisteredAt.Ticks % 10 != 0 || write.RegisteredAt > clock.UtcNow
            || !await actors.VerifyAsync(write.ActorId, ct)) return null;
        var parent = await organizations.FindOrganizationAsync(write.OrganizationId, ct);
        var member = await organizations.FindMembershipAsync(write.OrganizationId, write.ActorId, ct);
        var actor = await identities.FindUserByIdAsync(write.ActorId, ct);
        if (parent is not { Status: OrganizationStatus.Active } || actor is not { Status: AccountStatus.Active }
            || member is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || member.Id != write.MembershipId || member.Version != write.MembershipVersion || member.Role != write.Capacity
            || write.RegisteredAt < parent.UpdatedAt || write.RegisteredAt < member.UpdatedAt || write.RegisteredAt < actor.UpdatedAt
            || string.IsNullOrWhiteSpace(actor.DisplayName) || actor.DisplayName.Length > 160 || actor.DisplayName.Any(char.IsControl)) return null;
        var producer = Producer(write.OrganizationId, write.Kind, write.RecordId, ct);
        if (producer is null || producer.SourceVersion != write.SourceVersion || producer.CardVersion != write.CardVersion
            || producer.SourceVersion < 1 || producer.CardVersion < 1 || producer.ListVersion < 1 || write.RegisteredAt < producer.CreatedAt
            || !Retained(producer.CardState) || !Retained(producer.ListState) || !Retained(producer.SourceState)
            || !await work.AcquireBoardReadScopeAsync(write.OrganizationId, write.ActorId, producer.BoardId, ct)) return null;
        var access = await boards.GetSyncScopeAsync(producer.BoardId, write.ActorId, ct);
        if (access.Value is null || access.Value.Board.OrganizationId != write.OrganizationId || access.Value.Board.Id != producer.BoardId
            || !access.Value.Access.CanView || !access.Value.Access.CanAdminister
            || access.Value.Board.LifecycleState is not (BoardLifecycleState.Active or BoardLifecycleState.Archived)
            || Producer(write.OrganizationId, write.Kind, write.RecordId, ct) != producer || !await actors.VerifyAsync(write.ActorId, ct)) return null;
        var reference = new RetentionSourceReference(write.OrganizationId, write.RecordClass, write.RecordId, write.Kind,
            producer.ParentCardId, 1, producer.BoardId, producer.SourceVersion);
        var capacity = member.Role == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN";
        await ((InMemoryWorkManagementStore)work).AppendAuditFactAsync(write.EventId, write.OrganizationId, write.ActorId,
            "RECORD_RETENTION_SOURCE_REGISTERED", "RECORD_RETENTION_SOURCE", write.RecordId, write.CorrelationId, write.RegisteredAt, ct,
            sourceMetadata: new(write.RecordClass == RetentionRecordClass.OperationalWork ? "OPERATIONAL_WORK" : "DOCUMENT",
                write.Kind == RetentionProducerKind.Card ? "CARD" : write.Kind == RetentionProducerKind.CardComment ? "CARD_COMMENT" : "ATTACHMENT", capacity));
        ct.ThrowIfCancellationRequested();
        rows.Add(key, new(reference, write, actor.DisplayName));
        return reference;
    }
    private DemoRetentionProducerReference? Producer(Guid organization, RetentionProducerKind kind, Guid record, CancellationToken ct) =>
        work is InMemoryWorkManagementStore memory ? memory.ReadRetentionProducer(organization, kind, record, ct)
            : throw new InvalidOperationException("Demo retention requires actual Demo producers.");
    public Action CaptureRollback() => DemoRollback.Dictionary(rows);
    private static bool Retained(WorkItemLifecycleState value) => value is WorkItemLifecycleState.Active or WorkItemLifecycleState.Archived;
    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !scope.OwnsOrganizationCommand(organization)) throw new InvalidOperationException("Retention sources require an owning transaction.");
    }
}
