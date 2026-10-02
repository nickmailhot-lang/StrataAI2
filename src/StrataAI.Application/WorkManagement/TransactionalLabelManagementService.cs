namespace StrataAI.Application.WorkManagement;

public sealed partial class TransactionalWorkManagementService
{
    public Task<WorkOperation<BoardLabelPage>> ListLabelsAsync(Guid boardId, Guid actorId, Guid? after = null, CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorId, "view", WorkCommand.Create(actorId, null, "ListLabelsAsync", boardId, new { }, "board_not_found"), async () =>
        {
            var result = await inner.ListLabelsAsync(boardId, actorId, after, cancellationToken);
            return result.Succeeded && !await actors.VerifyAsync(actorId, cancellationToken)
                ? WorkOperation<BoardLabelPage>.Failure("session_unavailable") : result;
        }, cancellationToken);
    public Task<WorkOperation<BoardLabelRecord>> CreateLabelAsync(Guid boardId, Guid actorId, string name, string color,
        string correlationId, CancellationToken cancellationToken = default) =>
        ActiveLabelBoardCommand(boardId, actorId, name, color, correlationId, cancellationToken);
    private async Task<WorkOperation<BoardLabelRecord>> ActiveLabelBoardCommand(Guid boardId, Guid actorId, string name, string color, string correlationId, CancellationToken ct)
    {
        var board = await store.FindBoardAsync(boardId, ct);
        if (board is null) return WorkOperation<BoardLabelRecord>.Failure("board_not_found");
        return await transactions.ExecuteAsync(board.OrganizationId,
            WorkCommand.Create(actorId, context.IdempotencyKey, "CreateLabelAsync", boardId, new { name, color }, "board_not_found"),
            async _ => await AuthorizeBoard(boardId, actorId, "edit", ct)
                && (await store.FindBoardAsync(boardId, ct))?.LifecycleState == BoardLifecycleState.Active,
            () => inner.CreateLabelAsync(boardId, actorId, name, color, correlationId, ct), ct);
    }
    public Task<WorkOperation<BoardLabelRecord>> UpdateLabelAsync(Guid labelId, Guid actorId, string name, string color,
        string? rank, long version, string correlationId, CancellationToken cancellationToken = default) =>
        LabelCommand(labelId, actorId, "edit", WorkCommand.Create(actorId, context.IdempotencyKey, "UpdateLabelAsync", labelId,
            new { name, color, rank, version }, "label_not_found"), () => inner.UpdateLabelAsync(labelId, actorId, name, color, rank, version, correlationId, cancellationToken), cancellationToken);
    public Task<WorkOperation<BoardLabelRecord>> DeleteLabelAsync(Guid labelId, Guid actorId, long version,
        bool confirmed, string correlationId, CancellationToken cancellationToken = default) =>
        LabelCommand(labelId, actorId, "admin", WorkCommand.Create(actorId, context.IdempotencyKey, "DeleteLabelAsync", labelId,
            new { version, confirmed }, "label_not_found"), () => inner.DeleteLabelAsync(labelId, actorId, version, confirmed, correlationId, cancellationToken), cancellationToken, true);
    private async Task<WorkOperation<BoardLabelRecord>> LabelCommand(Guid id, Guid actor, string permission, WorkCommand command,
        Func<Task<WorkOperation<BoardLabelRecord>>> operation, CancellationToken ct, bool includeDeleted = false)
    {
        var label = await store.FindLabelAsync(id, ct, includeDeleted);
        if (label is null) return WorkOperation<BoardLabelRecord>.Failure("label_not_found");
        return await transactions.ExecuteAsync(label.OrganizationId, command, async _ =>
            await AuthorizeBoard(label.BoardId, actor, permission, ct)
            && (await store.FindBoardAsync(label.BoardId, ct))?.LifecycleState == BoardLifecycleState.Active,
            operation, ct);
    }
}
