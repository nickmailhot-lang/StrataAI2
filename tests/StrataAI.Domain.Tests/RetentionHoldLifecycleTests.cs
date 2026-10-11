using StrataAI.Domain.Records;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class RetentionHoldLifecycleTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    private static RetentionHold Applied() => RetentionHoldLifecycle.Apply(Guid.NewGuid(),
        new(Organization, RetentionRecordClass.Document, "contracts", Guid.NewGuid()), Guid.NewGuid(), "Reviewed hold source", At).Hold!;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PRD_70_hold_apply_preserves_explicit_Organization_class_category_and_record_scopes(int kind)
    {
        var scope = kind switch
        {
            0 => new RetentionHoldScope(Organization, null, null, null),
            1 => new(Organization, RetentionRecordClass.Governance, null, null),
            2 => new(Organization, RetentionRecordClass.Governance, "decisions", null),
            _ => new(Organization, RetentionRecordClass.Governance, null, Guid.NewGuid())
        };
        var id = Guid.NewGuid(); var actor = Guid.NewGuid();
        var result = RetentionHoldLifecycle.Apply(id, scope, actor, "Explicit human reason", At);
        Assert.True(result.Succeeded); Assert.Equal(RetentionHoldChangeReason.Applied, result.Reason);
        Assert.Equal(new RetentionHold(id, Organization, 1, scope.RecordClass, scope.Category, scope.RecordId,
            RetentionHoldStatus.Active, "Explicit human reason", actor, At, null, null), result.Hold);
        Assert.Null(RecordRetentionRules.Validate(result.Hold!));
    }

    [Fact]
    public void PRD_70_hold_apply_refuses_invalid_identity_scope_reason_and_clock()
    {
        var actor = Guid.NewGuid(); var id = Guid.NewGuid(); var scope = new RetentionHoldScope(Organization, null, null, null);
        foreach (var invalid in new[] { scope with { OrganizationId = Guid.Empty }, scope with { Category = "contracts" },
            scope with { RecordId = Guid.NewGuid() }, scope with { RecordClass = (RetentionRecordClass)99 },
            scope with { RecordClass = RetentionRecordClass.Document, Category = "bad category" },
            scope with { RecordClass = RetentionRecordClass.Document, RecordId = Guid.Empty } })
            Invalid(RetentionHoldLifecycle.Apply(id, invalid, actor, "Reason", At));
        Invalid(RetentionHoldLifecycle.Apply(Guid.Empty, scope, actor, "Reason", At));
        Invalid(RetentionHoldLifecycle.Apply(id, scope, Guid.Empty, "Reason", At));
        foreach (var reason in new[] { "", " Reason", "\0", new string('r', 2001) })
            Invalid(RetentionHoldLifecycle.Apply(id, scope, actor, reason, At));
        Invalid(RetentionHoldLifecycle.Apply(id, scope, actor, "Reason", At.ToOffset(TimeSpan.FromHours(1))));
    }

    [Fact]
    public void PRD_70_hold_release_retains_original_application_and_increments_exact_current_version()
    {
        var original = Applied(); var actor = Guid.NewGuid();
        var result = RetentionHoldLifecycle.Release(Organization, original, 1, actor, At.AddDays(1));
        Assert.True(result.Succeeded); Assert.Equal(RetentionHoldChangeReason.Released, result.Reason);
        Assert.Equal(original with { Version = 2, Status = RetentionHoldStatus.Released, ReleasedBy = actor, ReleasedAt = At.AddDays(1) }, result.Hold);
        Assert.Null(RecordRetentionRules.Validate(result.Hold!));
        Assert.Equal(RetentionHoldStatus.Active, original.Status); Assert.Null(original.ReleasedBy); Assert.Null(original.ReleasedAt);
        Assert.Equal(RetentionHoldChangeReason.VersionConflict, RetentionHoldLifecycle.Release(Organization, result.Hold!, 1, actor, At.AddDays(2)).Reason);
        var repeated = RetentionHoldLifecycle.Release(Organization, result.Hold!, 2, actor, At.AddDays(2));
        Assert.False(repeated.Succeeded); Assert.Null(repeated.Hold); Assert.Equal(RetentionHoldChangeReason.AlreadyReleased, repeated.Reason);
    }

    [Fact]
    public void PRD_70_hold_release_refuses_foreign_stale_future_invalid_and_overflow_sources()
    {
        var original = Applied(); var actor = Guid.NewGuid();
        Invalid(RetentionHoldLifecycle.Release(Guid.NewGuid(), original, 1, actor, At));
        Invalid(RetentionHoldLifecycle.Release(Guid.Empty, original, 1, actor, At));
        Invalid(RetentionHoldLifecycle.Release(Organization, original, 1, Guid.Empty, At));
        Invalid(RetentionHoldLifecycle.Release(Organization, original, 1, actor, At.AddTicks(-1)));
        Invalid(RetentionHoldLifecycle.Release(Organization, original, 1, actor, At.ToOffset(TimeSpan.FromHours(1))));
        Invalid(RetentionHoldLifecycle.Release(Organization, original with { Version = long.MaxValue }, long.MaxValue, actor, At));
        Invalid(RetentionHoldLifecycle.Release(Organization, original with { ReleasedBy = actor }, 1, actor, At));
        foreach (var version in new long[] { -1, 0, 2, long.MaxValue })
        {
            var result = RetentionHoldLifecycle.Release(Organization, original, version, actor, At);
            Assert.Equal(RetentionHoldChangeReason.VersionConflict, result.Reason); Assert.False(result.Succeeded); Assert.Null(result.Hold);
        }
    }

    [Fact]
    public void PRD_70_duplicate_hold_identities_or_revisions_cannot_establish_disposition_scope()
    {
        var record = new RetentionRecord(Guid.NewGuid(), Organization, 1, RetentionRecordClass.Document, "contracts", null, At, null, null, null, false);
        var policy = new RecordRetentionPolicy(Guid.NewGuid(), Organization, 1, record.RecordClass, record.Category, null,
            RetentionAnchor.Created, 0, RecordDispositionAction.Delete, "Explicit reviewed source", null, At, At);
        var original = Applied() with { RecordId = record.Id };
        var released = RetentionHoldLifecycle.Release(Organization, original, 1, Guid.NewGuid(), At).Hold!;
        Assert.Equal(DispositionReason.ActiveHold, RecordDisposition.Evaluate(policy, record, [original], true, At).Reason);
        Assert.True(RecordDisposition.Evaluate(policy, record, [released], true, At).CanDispose);
        foreach (var rows in new[] { new[] { released, released }, new[] { released, original }, new[] { original, released } })
        {
            var result = RecordDisposition.Evaluate(policy, record, rows, true, At);
            Assert.False(result.CanDispose); Assert.Equal(DispositionReason.InvalidSource, result.Reason);
            Assert.Equal(RecordDispositionAction.Preserve, result.Action);
        }
    }

    private static void Invalid(RetentionHoldChange result)
    { Assert.Equal(RetentionHoldChangeReason.InvalidSource, result.Reason); Assert.False(result.Succeeded); Assert.Null(result.Hold); }
}
