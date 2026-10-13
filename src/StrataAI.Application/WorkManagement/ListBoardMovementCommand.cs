using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

// Owning command boundary for the complete transfer/publication operation.
// Original source is explicit so a replay never derives its admission boundary
// from an already moved List. This does not expose a public move endpoint.
public sealed class ListBoardMovementCommand(IWorkManagementStore work, IWorkBoardAuthorization boards,
    IOrganizationStore organizations, ICommandActorAuthorization actors,
    IWorkManagementUnitOfWork transactions, IWorkCommandContext context)
{
    public async Task<WorkOperation<BoardListRecord>> ExecuteAsync(Guid listId, Guid actorId,
        Guid sourceBoardId, Guid destinationBoardId, long expectedVersion, Guid? beforeListId,
        Func<Task<WorkOperation<BoardListRecord>>> admittedOperation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(admittedOperation);
        if (expectedVersion <= 0 || expectedVersion == long.MaxValue)
            return WorkOperation<BoardListRecord>.Failure("invalid_version");
        if (listId == Guid.Empty || actorId == Guid.Empty || sourceBoardId == Guid.Empty
            || destinationBoardId == Guid.Empty || beforeListId == Guid.Empty)
            return WorkOperation<BoardListRecord>.Failure("list_not_found");
        var list = await work.FindListAsync(listId, ct);
        var source = await work.FindBoardAsync(sourceBoardId, ct);
        var destination = await work.FindBoardAsync(destinationBoardId, ct);
        if (list is null || source is null || destination is null || sourceBoardId == destinationBoardId
            || list.OrganizationId != source.OrganizationId || list.OrganizationId != destination.OrganizationId)
            return WorkOperation<BoardListRecord>.Failure("list_not_found");
        var organization = list.OrganizationId;
        var gates = new[] { sourceBoardId, destinationBoardId, list.BoardId }.Distinct().Order().ToArray();
        async Task<bool> HasCurrentAuthorityAsync()
        {
            foreach (var id in gates)
            {
                var admitted = await boards.GetSyncScopeAsync(id, actorId, ct);
                if (!admitted.Succeeded || admitted.Value is not { Access.CanMove: true, Board.LifecycleState: BoardLifecycleState.Active } scope
                    || scope.Board.OrganizationId != organization) return false;
            }
            return await organizations.FindOrganizationAsync(organization, ct) is { Status: OrganizationStatus.Active }
                && await actors.VerifyAsync(actorId, ct);
        }
        return await transactions.ExecuteAsync(organization,
            WorkCommand.Create(actorId, context.IdempotencyKey, "MoveListBoardAsync", listId,
                new { sourceBoardId, destinationBoardId, expectedVersion, beforeListId }, "list_not_found"), async receipt =>
            {
                foreach (var id in gates)
                {
                    if (!await work.AcquireCommandScopeAsync(organization, actorId, id, ct)) return false;
                }
                if (!await HasCurrentAuthorityAsync()) return false;
                var current = await work.FindListAsync(listId, ct);
                if (current is not { LifecycleState: WorkItemLifecycleState.Active } || current.OrganizationId != organization
                    || current.BoardId != list.BoardId) return false;
                return receipt is null || receipt.Id == listId && receipt.OrganizationId == organization
                    && receipt.BoardId == destinationBoardId && receipt.Version == expectedVersion + 1
                    && current.Version >= receipt.Version;
            }, async () =>
            {
                var current = await work.FindListAsync(listId, ct);
                if (current?.BoardId != sourceBoardId) return WorkOperation<BoardListRecord>.Failure("list_not_found");
                if (current.Version != expectedVersion) return WorkOperation<BoardListRecord>.Failure("version_conflict");
                var result = await admittedOperation();
                if (!await actors.VerifyAsync(actorId, ct)) return WorkOperation<BoardListRecord>.Failure("session_unavailable");
                if (!await HasCurrentAuthorityAsync()) return WorkOperation<BoardListRecord>.Failure("list_not_found");
                if (result.Succeeded && (result.Value is not { } changed || changed.Id != listId || changed.OrganizationId != organization
                    || changed.BoardId != destinationBoardId || changed.Version != expectedVersion + 1
                    || await work.FindListAsync(listId, ct) != changed))
                    throw new InvalidOperationException("List transfer returned an unrelated acknowledgment.");
                return result;
            }, ct);
    }
}
