using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed record DemoBoardAuthorityProof(Guid OrganizationId, Guid BoardId, string EventType,
    long BoardVersion, Guid SubjectId, long SubjectVersion, DateTimeOffset ChangedAt, Guid CommandId, BoardRole? PreviousRole)
{
    internal static bool Supports(WorkEvent source) => source.EntityType == "Board" && source.EntityId == source.BoardId
        && source.EventType is "BOARD_UPDATED" or "BOARD_VISIBILITY_CHANGED" or "BOARD_ARCHIVED" or "BOARD_RESTORED"
            or "BOARD_DELETED" or "BOARD_MEMBER_UPDATED" or "BOARD_MEMBER_REMOVED";
}

internal sealed partial class InMemoryWorkManagementStore
{
    private readonly Dictionary<(Guid Board, string Type), DemoBoardAuthorityProof> _boardAuthorityProofs = [];
    private void CaptureBoardAuthorityProof(BoardRecord board, string type, DateTimeOffset at,
        Guid? subject = null, long? subjectVersion = null, BoardRole? previousRole = null)
    {
        if (transactionScope.Owns(board.OrganizationId))
            _boardAuthorityProofs[(board.Id, type)] = new(board.OrganizationId, board.Id, type, board.Version,
                subject ?? board.Id, subjectVersion ?? board.Version, at, transactionScope.CommandId, previousRole);
    }
    internal DemoBoardAuthorityProof RequireBoardAuthorityProof(WorkEvent source)
    {
        if (!transactionScope.Owns(source.OrganizationId) || !DemoBoardAuthorityProof.Supports(source))
            throw new InvalidOperationException("Board authority source requires its owning command.");
        lock (_sync)
        {
            if (!_boardAuthorityProofs.TryGetValue((source.BoardId, source.EventType), out var proof)
                || proof.CommandId != transactionScope.CommandId || proof.OrganizationId != source.OrganizationId
                || proof.BoardVersion != source.Version || proof.SubjectVersion < 1 || proof.ChangedAt == default
                || source.CreatedAt < proof.ChangedAt
                || !_boards.TryGetValue(source.BoardId, out var board) || board.Version != proof.BoardVersion)
                throw new InvalidOperationException("Board authority transition is unproven.");
            if (source.EventType == "BOARD_DELETED" && (board.LifecycleState != BoardLifecycleState.Deleted || board.DeletedBy != source.ActorId))
                throw new InvalidOperationException("Board deletion authority actor is unproven.");
            if (proof.SubjectId != proof.BoardId && (!_members.TryGetValue((proof.BoardId, proof.SubjectId), out var member)
                || member.Version != proof.SubjectVersion || member.UpdatedAt != proof.ChangedAt
                || member.Active != (source.EventType == "BOARD_MEMBER_UPDATED")))
                throw new InvalidOperationException("Board membership authority transition is unproven.");
            return proof;
        }
    }
}
