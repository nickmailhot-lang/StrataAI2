namespace StrataAI.Domain.Records;

public enum RetentionRecordClass { OperationalWork, Governance, Audit, Correspondence, Document, Ai }
public enum RetentionAnchor { Created, Archived, Closed, Approved }
public enum RecordDispositionAction { Preserve, Delete, Anonymize }
public enum RetentionHoldStatus { Active, Released }

// Organization-specific policy values; elapsed UTC days are supplied explicitly.
// Null days means indefinite preservation, never a jurisdiction-derived default.
public sealed record RecordRetentionPolicy(Guid Id, Guid OrganizationId, long Version,
    RetentionRecordClass RecordClass, string Category, string? Jurisdiction,
    RetentionAnchor Anchor, int? RetentionDays, RecordDispositionAction Action,
    string Source, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// A null class/category/record scope denotes the whole owning Organization.
// Category and record scopes require a class to avoid cross-kind ID ambiguity.
public sealed record RetentionHold(Guid Id, Guid OrganizationId, long Version,
    RetentionRecordClass? RecordClass, string? Category, Guid? RecordId,
    RetentionHoldStatus Status, string Reason, Guid AppliedBy, DateTimeOffset AppliedAt,
    Guid? ReleasedBy, DateTimeOffset? ReleasedAt);

public sealed record RetentionRecord(Guid Id, Guid OrganizationId, long Version,
    RetentionRecordClass RecordClass, string Category, string? Jurisdiction,
    DateTimeOffset CreatedAt, DateTimeOffset? ArchivedAt, DateTimeOffset? ClosedAt,
    DateTimeOffset? ApprovedAt, bool Immutable);

public enum DispositionReason
{
    Eligible, InvalidSource, IncompleteHoldScope, NoApplicablePolicy,
    ImmutableRecord, ActiveHold, PolicyPreserves, MissingAnchor, RetentionPending, DeadlineOverflow,
    IncompletePolicyScope, PolicyChoiceRequired, PolicyReferenceUnavailable
}
public sealed record RecordDispositionDecision(bool CanDispose, RecordDispositionAction Action,
    DispositionReason Reason, DateTimeOffset? EligibleAt = null);

public static class RecordRetentionRules
{
    public const int MaximumRetentionDays = 3_652_500;
    public static string? Validate(RecordRetentionPolicy policy)
    {
        if (policy.Id == Guid.Empty || policy.OrganizationId == Guid.Empty) return "identity";
        if (policy.Version < 1) return "version";
        if (!Enum.IsDefined(policy.RecordClass) || !Key(policy.Category)) return "record_class";
        if (!OptionalText(policy.Jurisdiction, 120)) return "jurisdiction";
        if (!Enum.IsDefined(policy.Anchor)) return "anchor";
        if (policy.RetentionDays is < 0 or > MaximumRetentionDays) return "retention_days";
        if (!Enum.IsDefined(policy.Action) || policy.RetentionDays is null && policy.Action != RecordDispositionAction.Preserve) return "disposition";
        if (!Text(policy.Source, 1000, true) || !OptionalText(policy.Notes, 2000, true)) return "source";
        return Utc(policy.CreatedAt) && Utc(policy.UpdatedAt) && policy.UpdatedAt >= policy.CreatedAt ? null : "timestamps";
    }
    public static string? Validate(RetentionHold hold)
    {
        if (hold.Id == Guid.Empty || hold.OrganizationId == Guid.Empty || hold.AppliedBy == Guid.Empty) return "identity";
        if (hold.Version < 1) return "version";
        if (hold.RecordClass is { } kind && !Enum.IsDefined(kind) || hold.Category is not null && !Key(hold.Category)
            || hold.RecordId == Guid.Empty || (hold.Category is not null || hold.RecordId is not null) && hold.RecordClass is null) return "scope";
        if (!Enum.IsDefined(hold.Status)) return "status";
        if (!Text(hold.Reason, 2000, true) || !Utc(hold.AppliedAt)) return "provenance";
        if (hold.Status == RetentionHoldStatus.Active)
            return hold.ReleasedBy is null && hold.ReleasedAt is null ? null : "release";
        return hold.Version >= 2 && hold.ReleasedBy is { } actor && actor != Guid.Empty
            && hold.ReleasedAt is { } at && Utc(at) && at >= hold.AppliedAt ? null : "release";
    }
    public static string? Validate(RetentionRecord record)
    {
        if (record.Id == Guid.Empty || record.OrganizationId == Guid.Empty || record.Version < 1) return "identity";
        if (!Enum.IsDefined(record.RecordClass) || !Key(record.Category)) return "record_class";
        if (!OptionalText(record.Jurisdiction, 120)) return "jurisdiction";
        if (!Utc(record.CreatedAt)) return "timestamps";
        foreach (var at in new[] { record.ArchivedAt, record.ClosedAt, record.ApprovedAt })
            if (at is { } value && (!Utc(value) || value < record.CreatedAt)) return "timestamps";
        return null;
    }
    private static bool Utc(DateTimeOffset at) => at.Offset == TimeSpan.Zero;
    private static bool Key(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
    private static bool OptionalText(string? value, int max, bool multiline = false) => value is null || Text(value, max, multiline);
    private static bool Text(string? value, int max, bool multiline = false) => value is { Length: > 0 }
        && value.Length <= max && value.Trim() == value && !string.IsNullOrWhiteSpace(value)
        && !value.Any(c => char.IsControl(c) && !(multiline && c is '\r' or '\n' or '\t'));
}

// Decision primitive only. A caller must authorize, load the complete current
// scope and hold locks through the actual disposition transaction and audit.
public static class RecordDisposition
{
    // Re-read and lock both scopes in the eventual owning transaction. Preserve
    // the original explicit/automatic selection and complete record/rule snapshot.
    public static RecordDispositionDecision Evaluate(RetentionPolicySelection selection, RetentionRecord currentRecord,
        IReadOnlyList<RecordRetentionPolicy> currentPolicies, bool policyScopeComplete,
        IReadOnlyList<RetentionHold> currentHolds, bool holdScopeComplete, DateTimeOffset now)
    {
        RecordDispositionDecision Keep(RetentionPolicySelectionReason reason) => new(false, RecordDispositionAction.Preserve, reason switch
        {
            RetentionPolicySelectionReason.IncompleteScope => DispositionReason.IncompletePolicyScope,
            RetentionPolicySelectionReason.NoApplicablePolicy => DispositionReason.NoApplicablePolicy,
            RetentionPolicySelectionReason.ChoiceRequired => DispositionReason.PolicyChoiceRequired,
            RetentionPolicySelectionReason.ReferenceUnavailable => DispositionReason.PolicyReferenceUnavailable,
            _ => DispositionReason.InvalidSource
        });
        if (!selection.Succeeded) return Keep(selection.Reason);
        if (selection.Record != currentRecord) return Keep(RetentionPolicySelectionReason.InvalidSource);
        var current = RetentionPolicies.Select(currentRecord, currentPolicies, policyScopeComplete, now, selection.Reference);
        if (!current.Succeeded) return Keep(current.Reason);
        if (current.Policy != selection.Policy) return Keep(RetentionPolicySelectionReason.ReferenceUnavailable);
        return Evaluate(current.Policy!, currentRecord, currentHolds, holdScopeComplete, now);
    }
    public static RecordDispositionDecision Evaluate(RecordRetentionPolicy policy, RetentionRecord record,
        IReadOnlyList<RetentionHold> holds, bool holdScopeComplete, DateTimeOffset now)
    {
        RecordDispositionDecision Keep(DispositionReason reason, DateTimeOffset? eligible = null) =>
            new(false, RecordDispositionAction.Preserve, reason, eligible);
        if (RecordRetentionRules.Validate(policy) is not null || RecordRetentionRules.Validate(record) is not null
            || policy.OrganizationId != record.OrganizationId || now.Offset != TimeSpan.Zero
            || now < policy.UpdatedAt || now < record.CreatedAt) return Keep(DispositionReason.InvalidSource);
        if (!holdScopeComplete) return Keep(DispositionReason.IncompleteHoldScope);
        foreach (var hold in holds)
            if (RecordRetentionRules.Validate(hold) is not null || hold.OrganizationId != record.OrganizationId
                || hold.AppliedAt > now || hold.ReleasedAt > now) return Keep(DispositionReason.InvalidSource);
        if (policy.RecordClass != record.RecordClass || !Same(policy.Category, record.Category)
            || policy.Jurisdiction is not null && !Same(policy.Jurisdiction, record.Jurisdiction)) return Keep(DispositionReason.NoApplicablePolicy);
        if (record.Immutable) return Keep(DispositionReason.ImmutableRecord);
        if (holds.Any(h => h.Status == RetentionHoldStatus.Active
            && (h.RecordClass is null || h.RecordClass == record.RecordClass)
            && (h.Category is null || Same(h.Category, record.Category))
            && (h.RecordId is null || h.RecordId == record.Id))) return Keep(DispositionReason.ActiveHold);
        if (policy.Action == RecordDispositionAction.Preserve || policy.RetentionDays is null) return Keep(DispositionReason.PolicyPreserves);
        var anchor = policy.Anchor switch { RetentionAnchor.Created => record.CreatedAt,
            RetentionAnchor.Archived => record.ArchivedAt, RetentionAnchor.Closed => record.ClosedAt,
            RetentionAnchor.Approved => record.ApprovedAt, _ => null };
        if (anchor is null) return Keep(DispositionReason.MissingAnchor);
        DateTimeOffset eligible;
        try { eligible = anchor.Value.AddDays(policy.RetentionDays.Value); }
        catch (ArgumentOutOfRangeException) { return Keep(DispositionReason.DeadlineOverflow); }
        return now < eligible ? Keep(DispositionReason.RetentionPending, eligible)
            : new(true, policy.Action, DispositionReason.Eligible, eligible);
    }
    private static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
