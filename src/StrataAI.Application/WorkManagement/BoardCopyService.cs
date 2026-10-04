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
        BoardBackgroundImage? background = null;
        if (scope.Value.Board.BackgroundType == "IMAGE")
        {
            if (!Guid.TryParseExact(scope.Value.Board.BackgroundValue, "D", out var imageId)
                || (background = await store.FindBoardBackgroundImageAsync(scope.Value.Board.OrganizationId, boardId, imageId, ct)) is null)
                return WorkOperation<BoardRecord>.Failure("invalid_background");
        }
        else if (scope.Value.Board.BackgroundType != "COLOR") return WorkOperation<BoardRecord>.Failure("invalid_background");
        var created = await CreateBoardAsync(scope.Value.Board.OrganizationId, actorId, normalized,
            scope.Value.Board.Description, BoardVisibility.Private, "COLOR",
            background is null ? scope.Value.Board.BackgroundValue : null, correlationId, ct);
        if (!created.Succeeded || created.Value is null) return created;
        var target = created.Value;
        if (background is not null)
        {
            var owned = await store.CreateBoardBackgroundImageAsync(new(Guid.NewGuid(), target.OrganizationId,
                target.Id, actorId, target.CreatedAt, background.Preview, background.Id), ct);
            var initialized = await store.InitializeCopiedBoardBackgroundAsync(target.OrganizationId, target.Id,
                owned.Id, target.CreatedAt, ct);
            if (initialized is null) return WorkOperation<BoardRecord>.Failure("version_conflict");
            target = initialized;
        }
        await store.CopyBoardContentsAsync(boardId, target.Id, target.CreatedAt, ct);
        await RecordChangeAsync(target.OrganizationId, target.Id, actorId, "BOARD_COPIED", "Board",
            target.Id, target.Version, correlationId, ct);
        return WorkOperation<BoardRecord>.Success(target);
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
