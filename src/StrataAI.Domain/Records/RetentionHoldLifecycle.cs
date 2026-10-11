namespace StrataAI.Domain.Records;

public sealed record RetentionHoldScope(Guid OrganizationId, RetentionRecordClass? RecordClass,
    string? Category, Guid? RecordId);
public enum RetentionHoldChangeReason { Applied, Released, InvalidSource, VersionConflict, AlreadyReleased }
public sealed record RetentionHoldChange(RetentionHoldChangeReason Reason, RetentionHold? Hold = null)
{
    public bool Succeeded => Reason is RetentionHoldChangeReason.Applied or RetentionHoldChangeReason.Released && Hold is not null;
}

// State transitions only. Owning commands must authorize scope/record access,
// lock current sources and atomically persist revision, receipt and audit.
public static class RetentionHoldLifecycle
{
    public static RetentionHoldChange Apply(Guid id, RetentionHoldScope scope, Guid actor,
        string reason, DateTimeOffset now)
    {
        var hold = new RetentionHold(id, scope.OrganizationId, 1, scope.RecordClass, scope.Category,
            scope.RecordId, RetentionHoldStatus.Active, reason, actor, now, null, null);
        return RecordRetentionRules.Validate(hold) is null
            ? new(RetentionHoldChangeReason.Applied, hold) : new(RetentionHoldChangeReason.InvalidSource);
    }

    public static RetentionHoldChange Release(Guid organization, RetentionHold current, long expectedVersion,
        Guid actor, DateTimeOffset now)
    {
        if (organization == Guid.Empty || current.OrganizationId != organization
            || RecordRetentionRules.Validate(current) is not null || actor == Guid.Empty
            || now.Offset != TimeSpan.Zero || current.AppliedAt > now || current.ReleasedAt > now)
            return new(RetentionHoldChangeReason.InvalidSource);
        if (expectedVersion < 1 || expectedVersion != current.Version)
            return new(RetentionHoldChangeReason.VersionConflict);
        if (current.Status == RetentionHoldStatus.Released)
            return new(RetentionHoldChangeReason.AlreadyReleased);
        if (current.Version == long.MaxValue) return new(RetentionHoldChangeReason.InvalidSource);
        // Identity, scope, reason and original actor/clock are never rewritten.
        var released = current with { Version = current.Version + 1, Status = RetentionHoldStatus.Released,
            ReleasedBy = actor, ReleasedAt = now };
        return new(RetentionHoldChangeReason.Released, released);
    }
}
