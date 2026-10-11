namespace StrataAI.Domain.Records;

public sealed record RetentionPolicyReference(Guid OrganizationId, Guid PolicyId, long Version);
public enum RetentionPolicySelectionReason { Selected, InvalidSource, IncompleteScope, NoApplicablePolicy, ChoiceRequired, ReferenceUnavailable }

// Produced from owning sources, never deserialized from a disposition request.
public sealed class RetentionPolicySelection
{
    public RetentionPolicySelectionReason Reason { get; }
    public RetentionRecord Record { get; }
    public RecordRetentionPolicy? Policy { get; }
    public RetentionPolicyReference? Reference { get; }
    public bool Succeeded => Reason == RetentionPolicySelectionReason.Selected && Policy is not null;
    internal RetentionPolicySelection(RetentionPolicySelectionReason reason, RetentionRecord record,
        RetentionPolicyReference? reference, RecordRetentionPolicy? policy = null)
    { Reason = reason; Record = record; Reference = reference; Policy = policy; }
}

public static class RetentionPolicies
{
    // The owning reader must establish the complete current class/category and
    // jurisdiction-or-Organization scope under locks. A page is not completeness.
    // No jurisdiction precedence, shortest period or permissive action is invented.
    public static RetentionPolicySelection Select(RetentionRecord record, IReadOnlyList<RecordRetentionPolicy> candidates,
        bool candidateScopeComplete, DateTimeOffset now, RetentionPolicyReference? reference = null)
    {
        RetentionPolicySelection Result(RetentionPolicySelectionReason reason, RecordRetentionPolicy? policy = null) => new(reason, record, reference, policy);
        if (RecordRetentionRules.Validate(record) is not null || now.Offset != TimeSpan.Zero || record.CreatedAt > now
            || record.ArchivedAt > now || record.ClosedAt > now || record.ApprovedAt > now)
            return Result(RetentionPolicySelectionReason.InvalidSource);
        if (!candidateScopeComplete) return Result(RetentionPolicySelectionReason.IncompleteScope);
        if (reference is not null && (reference.OrganizationId != record.OrganizationId || reference.PolicyId == Guid.Empty || reference.Version <= 0))
            return Result(RetentionPolicySelectionReason.ReferenceUnavailable);
        // The persisted namespace permits at most one Organization-wide and one
        // matching jurisdiction policy for this exact record class/category.
        if (candidates.Count > 2) return Result(RetentionPolicySelectionReason.InvalidSource);
        var ids = new HashSet<Guid>(); var scopes = new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in candidates)
        {
            if (RecordRetentionRules.Validate(policy) is not null || policy.OrganizationId != record.OrganizationId || policy.UpdatedAt > now
                || policy.RecordClass != record.RecordClass || !Same(policy.Category, record.Category)
                || policy.Jurisdiction is not null && !Same(policy.Jurisdiction, record.Jurisdiction)
                || !ids.Add(policy.Id) || !scopes.Add(policy.Jurisdiction)) return Result(RetentionPolicySelectionReason.InvalidSource);
        }
        if (reference is not null)
        {
            var selected = candidates.SingleOrDefault(policy => policy.Id == reference.PolicyId && policy.Version == reference.Version);
            return selected is null ? Result(RetentionPolicySelectionReason.ReferenceUnavailable) : Result(RetentionPolicySelectionReason.Selected, selected);
        }
        return candidates.Count switch
        {
            0 => Result(RetentionPolicySelectionReason.NoApplicablePolicy),
            1 => Result(RetentionPolicySelectionReason.Selected, candidates[0]),
            _ => Result(RetentionPolicySelectionReason.ChoiceRequired)
        };
    }
    private static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
