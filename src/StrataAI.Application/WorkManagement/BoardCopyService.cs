using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<BoardRecord>> CopyBoardAsync(Guid boardId, Guid actorId, string name,
        long version, string correlationId, CancellationToken ct = default)
    {
        var scope = await ResolveAccessAsync(boardId, actorId, ct);
        if (scope is not { Access.CanEdit: true, Board.LifecycleState: BoardLifecycleState.Active }
            || await organizationStore.FindOrganizationAsync(scope.Value.Board.OrganizationId, ct) is not { Status: OrganizationStatus.Active }
            || await organizationStore.FindMembershipAsync(scope.Value.Board.OrganizationId, actorId, ct) is not { Active: true })
            return WorkOperation<BoardRecord>.Failure("board_not_found");
        if (!TryNormalizeName(name, out var normalized)) return WorkOperation<BoardRecord>.Failure("invalid_board_name");
        if (version <= 0 || scope.Value.Board.Version != version) return WorkOperation<BoardRecord>.Failure("version_conflict");
        // Image references need the separate stored-image admission contract.
        // Do not widen that unfinished capability through duplication.
        if (scope.Value.Board.BackgroundType != "COLOR") return WorkOperation<BoardRecord>.Failure("invalid_background");
        var created = await CreateBoardAsync(scope.Value.Board.OrganizationId, actorId, normalized,
            scope.Value.Board.Description, BoardVisibility.Private, scope.Value.Board.BackgroundType,
            scope.Value.Board.BackgroundValue, correlationId, ct);
        if (!created.Succeeded || created.Value is null) return created;
        await store.CopyBoardContentsAsync(boardId, created.Value.Id, created.Value.CreatedAt, ct);
        await RecordChangeAsync(created.Value.OrganizationId, created.Value.Id, actorId, "BOARD_COPIED", "Board",
            created.Value.Id, created.Value.Version, correlationId, ct);
        return created;
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public async Task<WorkOperation<BoardRecord>> CopyBoardAsync(Guid boardId, Guid actorId, string name,
        long version, string correlationId, CancellationToken ct = default)
    {
        var source = await store.FindBoardAsync(boardId, ct);
        if (source is null) return WorkOperation<BoardRecord>.Failure("board_not_found");
        return await transactions.ExecuteAsync(source.OrganizationId,
            WorkCommand.Create(actorId, context.IdempotencyKey, "CopyBoardAsync", boardId, new { name, version }, "board_not_found"),
            async receipt =>
            {
                // Initial admission protects receipt selection without taking a
                // Board gate before the retained destination is known. The
                // operation/replay below plans its complete Board gate set.
                if (receipt is null)
                    return await AuthorizeOrganization(source.OrganizationId, actorId, ct)
                        && await inner.CheckCommandAccessAsync(boardId, actorId, "edit", ct)
                        && await store.FindBoardAsync(boardId, ct) is { LifecycleState: BoardLifecycleState.Active };
                if (receipt.Id == boardId || receipt.OrganizationId != source.OrganizationId || receipt.Version != 1) return false;
                foreach (var id in new[] { boardId, receipt.Id }.Order())
                    if (!await AuthorizeBoard(id, actorId, id == boardId ? "edit" : "view", ct)
                        || await store.FindBoardAsync(id, ct) is not { LifecycleState: BoardLifecycleState.Active } current
                        || current.OrganizationId != source.OrganizationId) return false;
                return await organizations.FindMembershipAsync(source.OrganizationId, actorId, ct) is { Active: true }
                    && await actors.VerifyAsync(actorId, ct);
            }, async () =>
            {
                if (!await AuthorizeBoard(boardId, actorId, "edit", ct)) return WorkOperation<BoardRecord>.Failure("board_not_found");
                var result = await inner.CopyBoardAsync(boardId, actorId, name, version, correlationId, ct);
                return await actors.VerifyAsync(actorId, ct) ? result : WorkOperation<BoardRecord>.Failure("session_unavailable");
            }, ct);
    }
}
