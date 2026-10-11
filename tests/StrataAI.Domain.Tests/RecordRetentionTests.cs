using StrataAI.Domain.Records;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class RecordRetentionTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.NewGuid();
    private static RecordRetentionPolicy Policy() => new(Guid.NewGuid(), Organization, 1,
        RetentionRecordClass.OperationalWork, "work.card", "CA-BC", RetentionAnchor.Created, 30,
        RecordDispositionAction.Delete, "Explicit fixture policy source", null, At, At);
    private static RetentionRecord Record() => new(Guid.NewGuid(), Organization, 1,
        RetentionRecordClass.OperationalWork, "work.card", "CA-BC", At, null, null, null, false);
    private static RetentionHold Hold() => new(Guid.NewGuid(), Organization, 1,
        null, null, null, RetentionHoldStatus.Active, "Explicit fixture hold reason", Guid.NewGuid(), At, null, null);
    private static RecordDispositionDecision Decide(RecordRetentionPolicy policy, RetentionRecord record,
        RetentionHold[]? holds = null, DateTimeOffset? now = null, bool complete = true) =>
        RecordDisposition.Evaluate(policy, record, holds ?? [], complete, now ?? At.AddDays(30));

    [Fact]
    public void PRD_70_explicit_retention_deadline_requires_full_period_and_preserves_revision_source()
    {
        var policy = Policy(); var record = Record();
        Assert.Null(RecordRetentionRules.Validate(policy)); Assert.Null(RecordRetentionRules.Validate(record));
        var early = Decide(policy, record, now: At.AddDays(30).AddTicks(-1));
        Assert.False(early.CanDispose); Assert.Equal(DispositionReason.RetentionPending, early.Reason);
        Assert.Equal(At.AddDays(30), early.EligibleAt);
        var due = Decide(policy, record);
        Assert.True(due.CanDispose); Assert.Equal(RecordDispositionAction.Delete, due.Action);
        Assert.Equal(DispositionReason.Eligible, due.Reason);
        Assert.Equal(1, policy.Version); Assert.Equal(1, record.Version);
    }

    [Theory]
    [InlineData(RetentionAnchor.Archived)]
    [InlineData(RetentionAnchor.Closed)]
    [InlineData(RetentionAnchor.Approved)]
    public void PRD_70_missing_configured_anchor_never_borrows_creation_clock(RetentionAnchor anchor)
    {
        var policy = Policy() with { Anchor = anchor }; var record = Record();
        Assert.Equal(DispositionReason.MissingAnchor, Decide(policy, record).Reason);
        var at = At.AddDays(10);
        record = anchor switch { RetentionAnchor.Archived => record with { ArchivedAt = at },
            RetentionAnchor.Closed => record with { ClosedAt = at }, _ => record with { ApprovedAt = at } };
        Assert.Equal(DispositionReason.RetentionPending, Decide(policy, record).Reason);
        Assert.True(Decide(policy, record, now: at.AddDays(30)).CanDispose);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PRD_70_active_Organization_class_category_and_record_holds_suspend_disposition(int scope)
    {
        var record = Record(); var hold = Hold();
        if (scope > 0) hold = hold with { RecordClass = record.RecordClass };
        if (scope == 2) hold = hold with { Category = record.Category.ToUpperInvariant() };
        if (scope == 3) hold = hold with { RecordId = record.Id };
        Assert.Null(RecordRetentionRules.Validate(hold));
        var result = Decide(Policy(), record, [hold]);
        Assert.False(result.CanDispose); Assert.Equal(DispositionReason.ActiveHold, result.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PRD_70_unrelated_same_Organization_holds_do_not_change_record_scope(int mismatch)
    {
        var record = Record(); var hold = Hold() with { RecordClass = record.RecordClass };
        hold = mismatch switch { 0 => hold with { RecordClass = RetentionRecordClass.Document },
            1 => hold with { Category = "work.other" }, _ => hold with { RecordId = Guid.NewGuid() } };
        Assert.True(Decide(Policy(), record, [hold]).CanDispose);
    }

    [Fact]
    public void PRD_70_released_hold_requires_release_actor_clock_and_revision_before_disposition()
    {
        var original = Hold();
        var released = original with { Status = RetentionHoldStatus.Released, Version = 2,
            ReleasedBy = Guid.NewGuid(), ReleasedAt = At.AddDays(1) };
        Assert.Null(RecordRetentionRules.Validate(released));
        Assert.True(Decide(Policy(), Record(), [released]).CanDispose);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), [released with { ReleasedBy = null }]).Reason);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), [released with { Version = 1 }]).Reason);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), [released with { ReleasedAt = At.AddDays(-1) }]).Reason);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), [released with { ReleasedAt = At.AddDays(31) }]).Reason);
        Assert.Null(original.ReleasedAt); Assert.Equal(RetentionHoldStatus.Active, original.Status);
    }

    [Fact]
    public void PRD_70_incomplete_or_foreign_hold_sources_fail_closed()
    {
        Assert.Equal(DispositionReason.IncompleteHoldScope, Decide(Policy(), Record(), complete: false).Reason);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), [Hold() with { OrganizationId = Guid.NewGuid() }]).Reason);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), [Hold() with { RecordId = Guid.NewGuid() }]).Reason);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), [Hold() with { AppliedAt = At.AddDays(31) }]).Reason);
    }

    [Fact]
    public void PRD_70_governance_never_inherits_operational_Card_policy_and_immutable_records_are_preserved()
    {
        var governance = Record() with { RecordClass = RetentionRecordClass.Governance };
        Assert.Equal(DispositionReason.NoApplicablePolicy, Decide(Policy(), governance).Reason);
        var policy = Policy() with { RecordClass = RetentionRecordClass.Governance };
        Assert.Equal(DispositionReason.ImmutableRecord, Decide(policy, governance with { Immutable = true }).Reason);
        Assert.Equal(DispositionReason.ImmutableRecord, Decide(Policy(), Record() with { Immutable = true }).Reason);
    }

    [Theory]
    [InlineData(RetentionRecordClass.Governance)]
    [InlineData(RetentionRecordClass.Audit)]
    [InlineData(RetentionRecordClass.Correspondence)]
    [InlineData(RetentionRecordClass.Document)]
    [InlineData(RetentionRecordClass.Ai)]
    public void PRD_70_each_record_class_requires_its_own_applicable_policy(RetentionRecordClass kind)
    {
        Assert.Equal(DispositionReason.NoApplicablePolicy, Decide(Policy(), Record() with { RecordClass = kind }).Reason);
        Assert.True(Decide(Policy() with { RecordClass = kind }, Record() with { RecordClass = kind }).CanDispose);
    }

    [Fact]
    public void PRD_70_explicit_scope_rejects_foreign_tenant_and_inapplicable_jurisdiction_category()
    {
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy() with { OrganizationId = Guid.NewGuid() }, Record()).Reason);
        Assert.Equal(DispositionReason.NoApplicablePolicy, Decide(Policy() with { Jurisdiction = "CA-ON" }, Record()).Reason);
        Assert.Equal(DispositionReason.NoApplicablePolicy, Decide(Policy() with { Category = "work.other" }, Record()).Reason);
        Assert.True(Decide(Policy() with { Jurisdiction = null, Category = "WORK.CARD" }, Record()).CanDispose);
    }

    [Fact]
    public void PRD_70_indefinite_and_explicit_preservation_do_not_allow_delete_or_anonymize()
    {
        var indefinite = Policy() with { RetentionDays = null, Action = RecordDispositionAction.Preserve };
        Assert.Equal(DispositionReason.PolicyPreserves, Decide(indefinite, Record()).Reason);
        Assert.Equal(DispositionReason.PolicyPreserves, Decide(Policy() with { Action = RecordDispositionAction.Preserve }, Record()).Reason);
        Assert.Equal("disposition", RecordRetentionRules.Validate(indefinite with { Action = RecordDispositionAction.Anonymize }));
        var anonymize = Decide(Policy() with { Action = RecordDispositionAction.Anonymize }, Record());
        Assert.True(anonymize.CanDispose); Assert.Equal(RecordDispositionAction.Anonymize, anonymize.Action);
    }

    [Fact]
    public void PRD_70_elapsed_UTC_days_and_unrepresentable_deadlines_have_explicit_behavior()
    {
        var leap = new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero);
        var policy = Policy() with { CreatedAt = leap, UpdatedAt = leap, RetentionDays = 365 };
        var record = Record() with { CreatedAt = leap };
        Assert.Equal(new DateTimeOffset(2025, 2, 28, 0, 0, 0, TimeSpan.Zero), Decide(policy, record, now: leap.AddDays(365)).EligibleAt);
        Assert.Equal(DispositionReason.DeadlineOverflow, Decide(Policy(), Record() with { CreatedAt = DateTimeOffset.MaxValue }, now: DateTimeOffset.MaxValue).Reason);
        Assert.Equal(DispositionReason.InvalidSource, Decide(Policy(), Record(), now: At.AddTicks(-1)).Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PRD_70_invalid_policy_values_never_authorize_disposition(int invalid)
    {
        var policy = Policy();
        policy = invalid switch { 0 => policy with { Id = Guid.Empty }, 1 => policy with { Category = "not a key" },
            2 => policy with { Source = "" }, 3 => policy with { RetentionDays = -1 },
            4 => policy with { RecordClass = (RetentionRecordClass)99 }, _ => policy with { UpdatedAt = At.AddDays(-1) } };
        Assert.NotNull(RecordRetentionRules.Validate(policy));
        Assert.Equal(DispositionReason.InvalidSource, Decide(policy, Record()).Reason);
    }
}
