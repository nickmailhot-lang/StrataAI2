using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Records;

internal sealed record DemoRetentionHoldRevision(RetentionHoldWrite Source, string ActorLabel);

// Activation still requires Production parity and actual typed scope resolution.
internal sealed class InMemoryRetentionHoldStore(IOrganizationStore organizations, IIdentityStore identities,
    ICommandActorAuthorization actors, IRetentionHoldScopeAuthorization scopes, DemoWorkTransactionScope scope,
    IWorkManagementStore work, IClock clock) : IRetentionHoldStore, IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid Organization, Guid Hold), RetentionHold> _current = [];
    private readonly Dictionary<(Guid Organization, Guid Hold, long Version), DemoRetentionHoldRevision> _history = [];
    private readonly Dictionary<(Guid Organization, Guid Actor, Guid Key), RetentionHoldReceipt> _receipts = [];
    private readonly HashSet<Guid> _eventIds = [];
    public Task<RetentionHoldReference?> ReadReferenceAsync(Guid organization, Guid hold, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(Reference(_current.GetValueOrDefault((organization, hold))));
    }
    public Task<RetentionHoldReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(Reference(_receipts.GetValueOrDefault((organization, actor, key))?.Result));
    }
    // Historical producer context is unavailable until Demo source capture exists.
    // Never synthesize it from a current Card route. Real work authorization refuses it.
    private static RetentionHoldReference? Reference(RetentionHold? hold) => hold is null ? null
        : new(hold.OrganizationId, hold.Id, hold.Version, hold.RecordClass, hold.Category is not null,
            hold.RecordId, null, null, null, null);

    public Task<RetentionHoldCandidates> ReadApplicableAsync(RetentionRecord record, CancellationToken ct)
    {
        RequireScope(record.OrganizationId, ct);
        var now = clock.UtcNow;
        if (RecordRetentionRules.Validate(record) is not null || now.Offset != TimeSpan.Zero
            || record.CreatedAt > now || record.ArchivedAt > now || record.ClosedAt > now || record.ApprovedAt > now)
            throw new ArgumentException("Invalid retention record projection.", nameof(record));
        // Current states only, including release evidence. Never select historical
        // Active rows or omit a current row merely because its status is Released.
        var rows = _current.Values.Where(h => h.OrganizationId == record.OrganizationId
            && (h.RecordClass is null || h.RecordClass == record.RecordClass)
            && (h.Category is null || string.Equals(h.Category, record.Category, StringComparison.OrdinalIgnoreCase))
            && (h.RecordId is null || h.RecordId == record.Id)).OrderBy(h => h.Id);
        return Task.FromResult(new RetentionHoldCandidates(record, rows));
    }

    public Task<RetentionHold?> ReadAsync(Guid organization, Guid hold, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(_current.GetValueOrDefault((organization, hold)));
    }
    public Task<RetentionHoldReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(_receipts.GetValueOrDefault((organization, actor, key)));
    }
    internal IReadOnlyList<DemoRetentionHoldRevision> ReadHistory(Guid organization, Guid hold, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return _history.Where(x => x.Key.Organization == organization && x.Key.Hold == hold)
            .OrderBy(x => x.Key.Version).Select(x => x.Value).ToArray();
    }
    public async Task<RetentionHoldWriteResult> WriteAsync(long expectedVersion, RetentionHoldWrite write, CancellationToken ct)
    {
        var hold = write.Hold; var receipt = write.Receipt;
        RequireScope(hold.OrganizationId, ct);
        var current = await ReadAsync(hold.OrganizationId, hold.Id, ct);
        if (expectedVersion < 0 || expectedVersion == long.MaxValue || (current?.Version ?? 0) != expectedVersion
            || hold.Version != expectedVersion + 1) return RetentionHoldWriteResult.VersionConflict;
        if (_receipts.ContainsKey((hold.OrganizationId, write.ActorId, receipt.Key))) return RetentionHoldWriteResult.KeyConflict;
        var at = hold.ReleasedAt ?? hold.AppliedAt;
        var holdScope = new RetentionHoldScope(hold.OrganizationId, hold.RecordClass, hold.Category, hold.RecordId);
        var transition = current is null
            ? RetentionHoldLifecycle.Apply(hold.Id, holdScope, write.ActorId, hold.Reason, at)
            : RetentionHoldLifecycle.Release(hold.OrganizationId, current, expectedVersion, write.ActorId, at);
        var parent = await organizations.FindOrganizationAsync(hold.OrganizationId, ct);
        var membership = await organizations.FindMembershipAsync(hold.OrganizationId, write.ActorId, ct);
        var subject = await identities.FindUserByIdAsync(write.ActorId, ct);
        if (!transition.Succeeded || transition.Hold != hold || !await actors.VerifyAsync(write.ActorId, ct)
            || !await scopes.VerifyAsync(holdScope, write.ActorId, ct)
            || parent is not { Status: OrganizationStatus.Active } || subject is not { Status: AccountStatus.Active }
            || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || membership.Id != write.MembershipId || membership.Version != write.MembershipVersion || membership.Role != write.ActingCapacity
            || write.EventId == Guid.Empty || _eventIds.Contains(write.EventId)
            || string.IsNullOrWhiteSpace(write.CorrelationId) || write.CorrelationId.Length > 256 || write.CorrelationId.Any(char.IsControl)
            || at.Offset != TimeSpan.Zero || at.Ticks % 10 != 0 || hold.AppliedAt.Ticks % 10 != 0
            || at < parent.UpdatedAt || at < membership.UpdatedAt || at > clock.UtcNow
            || receipt.ActorId != write.ActorId || receipt.Key == Guid.Empty || receipt.Result != hold
            || at > DateTimeOffset.MaxValue.AddHours(-24) || receipt.ExpiresAt != at.AddHours(24)
            || receipt.Fingerprint.Length != 64 || !receipt.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')
            || string.IsNullOrWhiteSpace(subject.DisplayName) || subject.DisplayName.Length > 160 || subject.DisplayName.Any(char.IsControl))
            return RetentionHoldWriteResult.SourceUnavailable;
        if (work is not InMemoryWorkManagementStore audits) throw new InvalidOperationException("Demo hold audit persistence is unavailable.");
        var capacity = write.ActingCapacity == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN";
        var scopeKind = hold.RecordId is not null ? "RECORD" : hold.Category is not null ? "CATEGORY" : hold.RecordClass is not null ? "CLASS" : "ORGANIZATION";
        await audits.AppendAuditFactAsync(write.EventId, hold.OrganizationId, write.ActorId,
            hold.Status == RetentionHoldStatus.Active ? "RETENTION_HOLD_APPLIED" : "RETENTION_HOLD_RELEASED",
            "RETENTION_HOLD", hold.Id, write.CorrelationId, at, ct, holdMetadata: new(hold.Version, hold.Status.ToString().ToUpperInvariant(), scopeKind, capacity));
        ct.ThrowIfCancellationRequested();
        _current[(hold.OrganizationId, hold.Id)] = hold;
        _history.Add((hold.OrganizationId, hold.Id, hold.Version), new(write, subject.DisplayName));
        _receipts.Add((hold.OrganizationId, write.ActorId, receipt.Key), receipt); _eventIds.Add(write.EventId);
        return RetentionHoldWriteResult.Written;
    }
    public Action CaptureRollback()
    {
        Action[] restore = [DemoRollback.Dictionary(_current), DemoRollback.Dictionary(_history),
            DemoRollback.Dictionary(_receipts), DemoRollback.Set(_eventIds)];
        return () => { foreach (var undo in restore) undo(); };
    }
    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !scope.OwnsOrganizationCommand(organization))
            throw new InvalidOperationException("Retention hold requires an owning transaction.");
    }
}
