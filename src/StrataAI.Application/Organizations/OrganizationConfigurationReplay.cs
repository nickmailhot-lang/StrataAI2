namespace StrataAI.Application.Organizations;

public sealed record OrganizationConfigurationEventCandidate(OrganizationConfigurationEvent Source,
    DateTimeOffset? ReadyAt);

// Configuration versions form their own tenant stream. Its owning reader must
// admit current Owner/Admin access; this pure window grants no read authority.
// It never borrows the all-member Organization metadata sequence or audience.
public sealed record OrganizationConfigurationEventWindow(long Position, bool HasMore, bool Pending,
    bool ResetRequired, IReadOnlyList<OrganizationConfigurationEventCandidate> Events)
{
    public static OrganizationConfigurationEventWindow Build(Guid organization, long since, long head,
        int limit, IReadOnlyList<OrganizationConfigurationEventCandidate> rows)
    {
        if (organization == Guid.Empty || since < 0 || head < 0 || limit is < 1 or > 100 || rows.Count > limit + 1)
            throw new ArgumentOutOfRangeException(nameof(since));
        if (since > head) return Reset();
        var position = since;
        List<OrganizationConfigurationEventCandidate> events = [];
        HashSet<Guid> identities = [];
        foreach (var row in rows)
        {
            var source = row.Source;
            if (position == long.MaxValue || source.Version != position + 1 || source.Version > head
                || source.OrganizationId != organization || source.ActorId == Guid.Empty || source.EventId == Guid.Empty
                || !identities.Add(source.EventId) || string.IsNullOrWhiteSpace(source.CorrelationId)
                || source.CorrelationId.Length > 256 || row.ReadyAt < source.CreatedAt)
                return Reset();
            // Publication may complete out of order. Never consume a later ready
            // revision through an earlier pending one, including page lookahead.
            if (row.ReadyAt is null) return new(position, false, true, false, events.ToArray());
            if (events.Count == limit) return new(position, true, false, false, events.ToArray());
            events.Add(row); position = source.Version;
        }
        return position < head ? Reset() : new(position, false, false, false, events.ToArray());

        static OrganizationConfigurationEventWindow Reset() => new(0, false, false, true, []);
    }
}
