using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Unregistered. Callers must own the Organization transaction and re-read their
// current/original receipt hint before private detail disclosure and commit.
public sealed class RetentionWorkAuthorization(IRetentionSourceReferenceReader references,
    IRetentionWorkSourceReferences sources, IWorkManagementStore work, IWorkBoardAuthorization boards,
    IOrganizationStore organizations, ICommandActorAuthorization actors) : IRetentionHoldScopeAuthorization
{
    public Task<bool> VerifyAsync(RetentionHoldReference reference, Guid actor, CancellationToken ct) => VerifyHoldAsync(reference, actor, ct);
    public Task<bool> VerifyAsync(RetentionHoldScope scope, Guid actor, CancellationToken ct) =>
        scope.RecordClass is { } kind && scope.RecordId is { } record
            ? VerifySourceAsync(scope.OrganizationId, kind, record, actor, ct) : Task.FromResult(false);
    public async Task<bool> VerifySourceAsync(Guid organization, RetentionRecordClass recordClass,
        Guid record, Guid actor, CancellationToken ct)
    {
        if (!await Admitted(organization, actor, ct)) return false;
        var original = await references.ReadSourceAsync(organization, recordClass, record, ct);
        return original is not null && await VerifyAsync(original, null, actor, ct);
    }

    public async Task<bool> VerifyHoldAsync(RetentionHoldReference hold, Guid actor, CancellationToken ct)
    {
        if (hold.HoldId == Guid.Empty || hold.Version < 1 || !await Admitted(hold.OrganizationId, actor, ct)
            || hold.RecordClass is { } kind && !Enum.IsDefined(kind)) return false;
        if (hold.RecordId is null)
            return false; // Broad hold scopes need their dedicated retention administration contract.
        if (hold.RecordClass is null || hold.RecordId == Guid.Empty || hold.ContextVersion != 1
            || hold.ClassificationBoardId is null || hold.ClassificationBoardId == Guid.Empty
            || hold.ApplicationBoardId is null || hold.ApplicationBoardId == Guid.Empty || hold.ApplicationSourceVersion is not > 0) return false;
        var original = await references.ReadSourceAsync(hold.OrganizationId, hold.RecordClass.Value, hold.RecordId.Value, ct);
        return original is not null && original.OriginalBoardId == hold.ClassificationBoardId
            && await VerifyAsync(original, hold.ApplicationBoardId, actor, ct);
    }

    private async Task<bool> VerifyAsync(RetentionSourceReference original, Guid? applicationBoard, Guid actor, CancellationToken ct)
    {
        if (original.ContextVersion != 1 || original.OriginalBoardId is null || original.OriginalBoardId == Guid.Empty
            || original.SourceVersion is not > 0 || original.RecordId == Guid.Empty
            || !Supported(original)) return false;
        var current = await sources.ReadAsync(original.OrganizationId, original.RecordClass, original.RecordId, ct);
        if (current is null || current.Classification != original || !Available(current)) return false;
        var gates = new[] { original.OriginalBoardId.Value, applicationBoard ?? original.OriginalBoardId.Value, current.BoardId }.Distinct().Order();
        foreach (var gate in gates)
        {
            // Read-scope locking retains archived Board evidence. Current active
            // Organization admission and explicit administer permission still apply.
            if (!await work.AcquireBoardReadScopeAsync(original.OrganizationId, actor, gate, ct)) return false;
            var scope = await boards.GetSyncScopeAsync(gate, actor, ct);
            if (scope.Value is null || scope.Value.Board.Id != gate || scope.Value.Board.OrganizationId != original.OrganizationId
                || !scope.Value.Access.CanView || !scope.Value.Access.CanAdminister
                || scope.Value.Board.LifecycleState is not (BoardLifecycleState.Active or BoardLifecycleState.Archived)) return false;
        }
        // Refuse a changed route, parent, lifecycle or producer revision after a
        // wait instead of silently switching to another destination's authority.
        return await references.ReadSourceAsync(original.OrganizationId, original.RecordClass, original.RecordId, ct) == original
            && await sources.ReadAsync(original.OrganizationId, original.RecordClass, original.RecordId, ct) == current
            && await Admitted(original.OrganizationId, actor, ct);
    }
    private static bool Supported(RetentionSourceReference reference) => reference.Kind switch
    {
        RetentionProducerKind.Card => reference.RecordClass == RetentionRecordClass.OperationalWork && reference.ParentCardId is null,
        RetentionProducerKind.CardComment => reference.RecordClass == RetentionRecordClass.OperationalWork && reference.ParentCardId is { } card && card != Guid.Empty,
        RetentionProducerKind.Attachment => reference.RecordClass == RetentionRecordClass.Document && reference.ParentCardId is { } card && card != Guid.Empty,
        _ => false // Audit origin and Governance/Correspondence/AI need owning modules.
    };
    private static bool Available(RetentionWorkSourceReference source) => source.CardId != Guid.Empty && source.BoardId != Guid.Empty
        && source.ListId != Guid.Empty && source.CardVersion > 0 && source.ListVersion > 0 && source.SourceVersion > 0
        && source.CardId == (source.Classification.Kind == RetentionProducerKind.Card ? source.Classification.RecordId : source.Classification.ParentCardId)
        && Retained(source.CardState) && Retained(source.ListState) && Retained(source.SourceState);
    private static bool Retained(WorkItemLifecycleState state) => state is WorkItemLifecycleState.Active or WorkItemLifecycleState.Archived;
    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return organization != Guid.Empty && actor != Guid.Empty && await actors.VerifyAsync(actor, ct)
            && (await organizations.FindOrganizationAsync(organization, ct)) is { Status: OrganizationStatus.Active }
            && (await organizations.FindMembershipAsync(organization, actor, ct)) is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
    }
}
