namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    internal bool RequiresVerifiedAssignmentEmail => identityPolicy.RequireVerifiedEmail;
    public async Task<WorkOperation<AssignableBoardMemberPage>> ListAssignableBoardMembersAsync(Guid boardId, Guid actorId,
        Guid? after = null, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        if (access is not { Access.CanView: true, OrganizationMembership.Active: true }
            || access.Value.Board.LifecycleState != BoardLifecycleState.Active)
            return WorkOperation<AssignableBoardMemberPage>.Failure("board_not_found");
        if (after == Guid.Empty) return WorkOperation<AssignableBoardMemberPage>.Failure("invalid_board_member_cursor");
        var rows = await store.ListAssignableBoardMembersAsync(boardId, after, identityPolicy.RequireVerifiedEmail, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<AssignableBoardMemberPage>.Success(new(access.Value.Board.OrganizationId, boardId, items,
            rows.Count > 50 ? items[^1].UserId : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public Task<WorkOperation<AssignableBoardMemberPage>> ListAssignableBoardMembersAsync(Guid boardId, Guid actorId,
        Guid? after = null, CancellationToken cancellationToken = default) =>
        BoardCommand(boardId, actorId, "view", WorkCommand.Create(actorId, null, "ListAssignableBoardMembersAsync", boardId, new { }, "board_not_found"), async () =>
        {
            var result = await inner.ListAssignableBoardMembersAsync(boardId, actorId, after, cancellationToken);
            return result.Succeeded && !await actors.VerifyAsync(actorId, cancellationToken)
                ? WorkOperation<AssignableBoardMemberPage>.Failure("session_unavailable") : result;
        }, cancellationToken);
}
