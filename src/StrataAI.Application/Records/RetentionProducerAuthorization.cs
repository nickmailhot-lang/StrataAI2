using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Records;

// Unregistered. Owning-transaction admission before first classification review.
// Callers must retain this exact evidence as their write precondition and repeat
// admission before disclosure/commit; this does not authorize a private body alone.
public sealed class RetentionProducerAuthorization(IRetentionProducerReferences producers,
    IWorkManagementStore work, IWorkBoardAuthorization boards,
    IOrganizationStore organizations, ICommandActorAuthorization actors)
{
    public async Task<bool> VerifyAsync(RetentionProducerReference expected, Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Available(expected) || !await Admitted(expected.OrganizationId, actor, ct)) return false;
        if (await producers.ReadAsync(expected.OrganizationId, expected.Kind, expected.RecordId, ct) != expected) return false;
        if (!await work.AcquireBoardReadScopeAsync(expected.OrganizationId, actor, expected.BoardId, ct)) return false;
        var scope = await boards.GetSyncScopeAsync(expected.BoardId, actor, ct);
        if (scope.Value is null || scope.Value.Board.Id != expected.BoardId
            || scope.Value.Board.OrganizationId != expected.OrganizationId
            || !scope.Value.Access.CanView || !scope.Value.Access.CanAdminister
            || scope.Value.Board.LifecycleState is not (BoardLifecycleState.Active or BoardLifecycleState.Archived)) return false;
        // A move, parent change, deletion or revision change after waiting must
        // produce a new review; never adopt another Board's authority implicitly.
        return await producers.ReadAsync(expected.OrganizationId, expected.Kind, expected.RecordId, ct) == expected
            && await Admitted(expected.OrganizationId, actor, ct);
    }

    private static bool Available(RetentionProducerReference source) => source.OrganizationId != Guid.Empty
        && source.RecordId != Guid.Empty && source.CardId != Guid.Empty && source.BoardId != Guid.Empty
        && source.ListId != Guid.Empty && source.CardVersion > 0 && source.ListVersion > 0 && source.SourceVersion > 0
        && source.CreatedAt.Offset == TimeSpan.Zero
        && Retained(source.CardState) && Retained(source.ListState) && Retained(source.SourceState)
        && (source.Kind switch
        {
            RetentionProducerKind.Card => source.ParentCardId is null && source.RecordId == source.CardId
                && source.SourceVersion == source.CardVersion && source.SourceState == source.CardState,
            RetentionProducerKind.CardComment or RetentionProducerKind.Attachment => source.ParentCardId == source.CardId,
            _ => false
        });
    private static bool Retained(WorkItemLifecycleState state) => state is WorkItemLifecycleState.Active or WorkItemLifecycleState.Archived;
    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) => actor != Guid.Empty
        && await actors.VerifyAsync(actor, ct)
        && (await organizations.FindOrganizationAsync(organization, ct)) is { Status: OrganizationStatus.Active }
        && (await organizations.FindMembershipAsync(organization, actor, ct)) is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
}
