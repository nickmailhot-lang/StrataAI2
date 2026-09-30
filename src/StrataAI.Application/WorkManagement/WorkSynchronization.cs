using System.Globalization;

namespace StrataAI.Application.WorkManagement;

public sealed record BoardSyncScope(BoardRecord Board, BoardAccess Access);
public interface IWorkBoardAuthorization
{
    Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid boardId, Guid? actorId,
        CancellationToken cancellationToken = default);
}

public sealed record WorkEventReadCandidate(long Sequence, WorkEvent Event, bool Ready, bool EntityVisible);
public sealed record WorkEventReadPage(long Cursor, bool HasMore, bool Pending, bool ResetRequired,
    IReadOnlyList<WorkEventReadCandidate> Events);
public interface IWorkEventReader
{
    Task<WorkEventReadPage> ReadAsync(Guid organizationId, Guid boardId, long since, int limit,
        CancellationToken cancellationToken = default);
}

// Sequence/cursor strings retain PostgreSQL bigint precision in browser clients.
public sealed record BoardSyncEvent(Guid EventId, Guid OrganizationId, Guid BoardId,
    Guid? ActorId, string EventType, string EntityType, Guid EntityId, long Version,
    string Sequence, DateTimeOffset CreatedAt, IReadOnlyDictionary<string, object?> Metadata);
public sealed record BoardSyncPage(string Cursor, bool HasMore, bool Pending, bool ResetRequired,
    IReadOnlyList<BoardSyncEvent> Events);

public static class WorkEventReadWindow
{
    // The adapter supplies an ordered, bounded limit+1 window, including pending
    // rows. Filtering to ready rows first would permanently skip a delayed event.
    public static WorkEventReadPage Build(long since, long published, int limit,
        IReadOnlyList<WorkEventReadCandidate> candidates)
    {
        if (since < 0 || published < 0 || limit is < 1 or > 100 || candidates.Count > limit + 1)
            throw new ArgumentOutOfRangeException(nameof(since));
        if (since > published) return Reset();
        var cursor = since;
        var events = new List<WorkEventReadCandidate>();
        foreach (var candidate in candidates)
        {
            if (cursor == long.MaxValue || candidate.Sequence != cursor + 1 || candidate.Sequence > published)
                return Reset();
            if (!candidate.Ready) return new(cursor, false, true, false, events);
            if (events.Count == limit) return new(cursor, true, false, false, events);
            events.Add(candidate);
            cursor = candidate.Sequence;
        }
        // A missing committed row is an explicit recovery boundary, not permission
        // to jump the cursor. Normal publication/rollback cannot create this gap.
        return cursor < published ? Reset() : new(cursor, false, false, false, events);
    }

    private static WorkEventReadPage Reset() => new(0, false, false, true, []);
}

public sealed class WorkSynchronizationService(IWorkBoardAuthorization authorization, IWorkEventReader events)
{
    public async Task<WorkOperation<BoardSyncPage>> ReadAsync(Guid boardId, Guid? actorId,
        long since, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (since < 0) return WorkOperation<BoardSyncPage>.Failure("invalid_sync_cursor");
        if (limit is < 1 or > 100) return WorkOperation<BoardSyncPage>.Failure("invalid_sync_limit");
        var initial = await authorization.GetSyncScopeAsync(boardId, actorId, cancellationToken);
        if (!Allowed(initial, boardId)) return WorkOperation<BoardSyncPage>.Failure("board_not_found");
        var organization = initial.Value!.Board.OrganizationId;
        var page = await events.ReadAsync(organization, boardId, since, limit, cancellationToken);
        // Revocation or visibility changes during the awaited read must invalidate
        // the entire response, including an empty page or recovery cursor.
        var current = await authorization.GetSyncScopeAsync(boardId, actorId, cancellationToken);
        if (!Allowed(current, boardId) || current.Value!.Board.OrganizationId != organization)
            return WorkOperation<BoardSyncPage>.Failure("board_not_found");
        if (!ValidPage(page, organization, boardId, since, limit))
            return WorkOperation<BoardSyncPage>.Failure("work_sync_unavailable");
        var board = current.Value.Board;
        var result = page.Events.Select(row =>
        {
            var change = row.Event;
            // Hidden/archived/deleted entities yield only a Board invalidation;
            // their historical IDs/type/version never reach an unauthorized view.
            var visible = row.EntityVisible && (change.EntityType != "Board" || change.EntityId == boardId);
            return new BoardSyncEvent(change.EventId, organization, boardId,
                visible && current.Value.Access.CanAdminister ? change.ActorId : null,
                visible ? change.EventType : "BOARD_INVALIDATED", visible ? change.EntityType : "Board",
                visible ? change.EntityId : boardId, visible ? change.Version : board.Version,
                row.Sequence.ToString(CultureInfo.InvariantCulture), change.CreatedAt,
                new Dictionary<string, object?>());
        }).ToArray();
        return WorkOperation<BoardSyncPage>.Success(new(page.Cursor.ToString(CultureInfo.InvariantCulture),
            page.HasMore, page.Pending, page.ResetRequired, result));
    }

    private static bool Allowed(WorkOperation<BoardSyncScope> scope, Guid boardId) =>
        boardId != Guid.Empty && scope.Succeeded && scope.Value is { Access.CanView: true } &&
        scope.Value.Board.Id == boardId && scope.Value.Board.OrganizationId != Guid.Empty;

    private static bool ValidPage(WorkEventReadPage page, Guid organization, Guid board, long since, int limit)
    {
        if (page.ResetRequired)
            return page.Cursor == 0 && page.Events.Count == 0 && !page.HasMore && !page.Pending;
        if (page.Events.Count > limit || page.Cursor < since || page.HasMore && (page.Pending || page.Events.Count != limit)) return false;
        var cursor = since;
        var identities = new HashSet<Guid>();
        foreach (var row in page.Events)
        {
            if (!row.Ready || cursor == long.MaxValue || row.Sequence != cursor + 1 ||
                row.Event.OrganizationId != organization || row.Event.BoardId != board ||
                row.Event.EventId == Guid.Empty || !identities.Add(row.Event.EventId) || row.Event.ActorId == Guid.Empty ||
                row.Event.EntityId == Guid.Empty || row.Event.Version < 1 ||
                row.Event.EntityType is not ("Board" or "List" or "Card")) return false;
            cursor = row.Sequence;
        }
        return cursor == page.Cursor;
    }
}
