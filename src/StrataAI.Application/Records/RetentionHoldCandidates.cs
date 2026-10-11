using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Storage result only, never accepted from HTTP. A synchronous lookup that exceeds
// its bound cannot establish completeness or authorize disposition.
public sealed class RetentionHoldCandidates
{
    public const int MaximumSynchronousCandidates = 1000;
    public RetentionRecord Record { get; }
    public IReadOnlyList<RetentionHold> Items { get; }
    public bool ScopeComplete { get; }
    public RetentionHoldCandidates(RetentionRecord record, IEnumerable<RetentionHold> completeScope)
    {
        Record = record;
        var rows = completeScope.Take(MaximumSynchronousCandidates + 1).ToArray();
        Items = Array.AsReadOnly(rows);
        ScopeComplete = rows.Length <= MaximumSynchronousCandidates;
    }
    // The caller must retain owning locks and re-read the authoritative record,
    // policies and holds through disposition. This result grants no permissions.
    public RecordDispositionDecision Evaluate(RetentionPolicySelection selection, RetentionRecord currentRecord,
        IReadOnlyList<RecordRetentionPolicy> currentPolicies, bool policyScopeComplete, DateTimeOffset now) =>
        Record != currentRecord
            ? new(false, RecordDispositionAction.Preserve, DispositionReason.InvalidSource)
            : RecordDisposition.Evaluate(selection, currentRecord, currentPolicies, policyScopeComplete, Items, ScopeComplete, now);
}
