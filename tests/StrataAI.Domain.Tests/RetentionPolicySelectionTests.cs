using StrataAI.Domain.Records;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class RetentionPolicySelectionTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    private static RetentionRecord Record() => new(Guid.NewGuid(), Guid.NewGuid(), 1, RetentionRecordClass.Governance,
        "minutes", "Explicit jurisdiction", At, null, At, null, false);
    private static RecordRetentionPolicy Policy(RetentionRecord record, string? jurisdiction = null) => new(Guid.NewGuid(), record.OrganizationId, 1,
        record.RecordClass, record.Category, jurisdiction, RetentionAnchor.Closed, 0, RecordDispositionAction.Delete, "Explicit reviewed source", null, At, At);
    private static RetentionPolicyReference Reference(RecordRetentionPolicy policy) => new(policy.OrganizationId, policy.Id, policy.Version);

    [Fact]
    public void PRD_70_selection_does_not_invent_jurisdiction_precedence_or_choose_shortest_period()
    {
        var record = Record(); var broad = Policy(record) with { Action = RecordDispositionAction.Preserve, RetentionDays = null };
        var specific = Policy(record, record.Jurisdiction);
        foreach (var policies in new[] { new[] { broad, specific }, new[] { specific, broad } })
        {
            var unresolved = RetentionPolicies.Select(record, policies, true, At);
            Assert.Equal(RetentionPolicySelectionReason.ChoiceRequired, unresolved.Reason); Assert.Null(unresolved.Policy);
            Assert.Equal(DispositionReason.PolicyChoiceRequired, RecordDisposition.Evaluate(unresolved, record, policies, true, [], true, At).Reason);
            var chosen = RetentionPolicies.Select(record, policies, true, At, Reference(broad));
            Assert.Equal(broad, chosen.Policy);
            Assert.Equal(DispositionReason.PolicyPreserves, RecordDisposition.Evaluate(chosen, record, policies, true, [], true, At).Reason);
        }
    }
    [Fact]
    public void PRD_70_complete_unique_source_selects_without_substituting_record_or_revision()
    {
        var record = Record(); var policy = Policy(record, record.Jurisdiction!.ToUpperInvariant()) with { Category = "MINUTES" };
        var selected = RetentionPolicies.Select(record, [policy], true, At);
        Assert.True(selected.Succeeded); Assert.Null(selected.Reference); Assert.Equal(record, selected.Record);
        Assert.True(RecordDisposition.Evaluate(selected, record, [policy], true, [], true, At).CanDispose);
        foreach (var changed in new[] { record with { Version = 2 }, record with { Id = Guid.NewGuid() }, record with { Immutable = true },
            record with { ClosedAt = At.AddTicks(1) }, record with { Category = "MINUTES" } })
            Assert.Equal(DispositionReason.InvalidSource, RecordDisposition.Evaluate(selected, changed, [policy], true, [], true, At.AddDays(1)).Reason);
    }
    [Fact]
    public void PRD_70_incomplete_and_empty_sources_never_enable_disposition()
    {
        var record = Record(); var policy = Policy(record);
        Assert.Equal(RetentionPolicySelectionReason.IncompleteScope, RetentionPolicies.Select(record, [policy], false, At).Reason);
        Assert.Equal(RetentionPolicySelectionReason.NoApplicablePolicy, RetentionPolicies.Select(record, [], true, At).Reason);
        var selected = RetentionPolicies.Select(record, [policy], true, At);
        Assert.Equal(DispositionReason.IncompletePolicyScope, RecordDisposition.Evaluate(selected, record, [policy], false, [], true, At).Reason);
        Assert.Equal(DispositionReason.IncompleteHoldScope, RecordDisposition.Evaluate(selected, record, [policy], true, [], false, At).Reason);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void PRD_70_wrong_foreign_future_duplicate_or_unrelated_candidate_is_refused(int invalid)
    {
        var record = Record(); var policy = Policy(record);
        RecordRetentionPolicy[] candidates = invalid switch
        {
            0 => [policy with { OrganizationId = Guid.NewGuid() }],
            1 => [policy with { UpdatedAt = At.AddTicks(1) }],
            2 => [policy with { RecordClass = RetentionRecordClass.OperationalWork }],
            3 => [policy with { Category = "other" }],
            4 => [policy with { Jurisdiction = "Different jurisdiction" }],
            5 => [policy, policy with { Id = Guid.NewGuid() }],
            _ => [policy, policy, policy]
        };
        var selected = RetentionPolicies.Select(record, candidates, true, At);
        Assert.Equal(RetentionPolicySelectionReason.InvalidSource, selected.Reason); Assert.False(selected.Succeeded);
    }
    [Fact]
    public void PRD_70_explicit_reference_cannot_rebind_to_latest_foreign_missing_or_nonpositive_revision()
    {
        var record = Record(); var policy = Policy(record);
        foreach (var reference in new[] { Reference(policy) with { OrganizationId = Guid.NewGuid() }, Reference(policy) with { Version = 2 },
            Reference(policy) with { PolicyId = Guid.NewGuid() }, Reference(policy) with { Version = 0 } })
            Assert.Equal(RetentionPolicySelectionReason.ReferenceUnavailable, RetentionPolicies.Select(record, [policy], true, At, reference).Reason);
        var selected = RetentionPolicies.Select(record, [policy], true, At, Reference(policy));
        Assert.Equal(DispositionReason.PolicyReferenceUnavailable, RecordDisposition.Evaluate(selected, record, [policy with { Version = 2 }], true, [], true, At).Reason);
        Assert.Equal(DispositionReason.PolicyReferenceUnavailable, RecordDisposition.Evaluate(selected, record, [policy with { Source = "Substituted same-version source" }], true, [], true, At).Reason);
    }
    [Fact]
    public void PRD_70_new_ambiguity_invalidates_automatic_choice_but_preserves_an_explicit_reviewed_choice()
    {
        var record = Record(); var broad = Policy(record); var specific = Policy(record, record.Jurisdiction);
        var automatic = RetentionPolicies.Select(record, [broad], true, At);
        var explicitChoice = RetentionPolicies.Select(record, [broad], true, At, Reference(broad));
        Assert.Equal(DispositionReason.PolicyChoiceRequired, RecordDisposition.Evaluate(automatic, record, [broad, specific], true, [], true, At).Reason);
        Assert.True(RecordDisposition.Evaluate(explicitChoice, record, [broad, specific], true, [], true, At).CanDispose);
    }
    [Fact]
    public void PRD_70_selected_policy_still_requires_hold_and_governance_preservation_checks()
    {
        var record = Record() with { Immutable = true }; var policy = Policy(record);
        var selected = RetentionPolicies.Select(record, [policy], true, At);
        Assert.Equal(DispositionReason.ImmutableRecord, RecordDisposition.Evaluate(selected, record, [policy], true, [], true, At).Reason);
        record = record with { Immutable = false }; selected = RetentionPolicies.Select(record, [policy], true, At);
        var hold = new RetentionHold(Guid.NewGuid(), record.OrganizationId, 1, null, null, null, RetentionHoldStatus.Active,
            "Reviewed hold", Guid.NewGuid(), At, null, null);
        Assert.Equal(DispositionReason.ActiveHold, RecordDisposition.Evaluate(selected, record, [policy], true, [hold], true, At).Reason);
        Assert.Equal(RetentionPolicySelectionReason.InvalidSource, RetentionPolicies.Select(record with { ApprovedAt = At.AddTicks(1) }, [policy], true, At).Reason);
    }
}
