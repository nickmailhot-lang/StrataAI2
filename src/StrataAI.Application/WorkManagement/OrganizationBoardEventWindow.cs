namespace StrataAI.Application.WorkManagement;

// Internal replay positions must be protected before being returned to a client:
// gaps can represent Boards outside the actor's administrative audience.
public sealed record OrganizationBoardEventCandidate(long Sequence, Guid EventId, Guid BoardId,
    string EventType, long Version, DateTimeOffset CreatedAt, bool Ready);
public sealed record OrganizationBoardEventPage(long Cursor, bool HasMore, bool Pending,
    bool ResetRequired, IReadOnlyList<OrganizationBoardEventCandidate> Events);

public static class OrganizationBoardEventWindow
{
    // The adapter filters administrative audience BEFORE applying limit+1, but
    // retains pending canonical sources. Other Boards legitimately leave gaps.
    public static OrganizationBoardEventPage Build(long since, long published, int limit,
        IReadOnlyList<OrganizationBoardEventCandidate> candidates)
    {
        if (since < 0 || published < 0 || limit is < 1 or > 100 || candidates.Count > limit + 1)
            throw new ArgumentOutOfRangeException(nameof(since));
        if (since > published) return new(0, false, false, true, []);
        var cursor = since;
        var events = new List<OrganizationBoardEventCandidate>();
        foreach (var row in candidates)
        {
            if (row.Sequence <= cursor || row.Sequence > published)
                return new(0, false, false, true, []);
            if (!row.Ready) return new(cursor, false, true, false, events);
            if (events.Count == limit) return new(cursor, true, false, false, events);
            events.Add(row);
            cursor = row.Sequence;
        }
        return new(published, false, false, false, events);
    }
}
