using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Records;

internal sealed record DemoRetentionPolicyRevision(RetentionPolicyWrite Source, string ActorLabel);

internal sealed class InMemoryRetentionPolicyStore(IOrganizationStore organizations, IIdentityStore identities,
    ICommandActorAuthorization actors, DemoWorkTransactionScope scope, IWorkManagementStore work)
    : IRetentionPolicyStore, IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid Organization, Guid Policy), RecordRetentionPolicy> _current = [];
    private readonly Dictionary<(Guid Organization, Guid Policy, long Version), DemoRetentionPolicyRevision> _history = [];
    private readonly Dictionary<(Guid Organization, Guid Actor, Guid Key), RetentionPolicyReceipt> _receipts = [];
    private readonly Dictionary<(Guid Organization, RetentionRecordClass Class, string Category, string Jurisdiction), Guid> _namespaces = [];
    private readonly HashSet<Guid> _eventIds = [];

    public Task<RecordRetentionPolicy?> ReadAsync(Guid organization, Guid policy, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(_current.GetValueOrDefault((organization, policy)));
    }

    public Task<RetentionPolicyReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(_receipts.GetValueOrDefault((organization, actor, key)));
    }

    internal IReadOnlyList<DemoRetentionPolicyRevision> ReadHistory(Guid organization, Guid policy, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return _history.Where(x => x.Key.Organization == organization && x.Key.Policy == policy)
            .OrderBy(x => x.Key.Version).Select(x => x.Value).ToArray();
    }

    public async Task<RetentionPolicyWriteResult> WriteAsync(long expectedVersion, RetentionPolicyWrite write, CancellationToken ct)
    {
        var policy = write.Policy; var receipt = write.Receipt;
        RequireScope(policy.OrganizationId, ct);
        var current = await ReadAsync(policy.OrganizationId, policy.Id, ct);
        if (expectedVersion < 0 || expectedVersion == long.MaxValue || (current?.Version ?? 0) != expectedVersion
            || policy.Version != expectedVersion + 1) return RetentionPolicyWriteResult.VersionConflict;
        if (_receipts.ContainsKey((policy.OrganizationId, write.ActorId, receipt.Key))) return RetentionPolicyWriteResult.KeyConflict;
        var parent = await organizations.FindOrganizationAsync(policy.OrganizationId, ct);
        var membership = await organizations.FindMembershipAsync(policy.OrganizationId, write.ActorId, ct);
        var subject = await identities.FindUserByIdAsync(write.ActorId, ct);
        if (!await actors.VerifyAsync(write.ActorId, ct) || parent is not { Status: OrganizationStatus.Active }
            || subject is not { Status: AccountStatus.Active }
            || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || membership.Id != write.MembershipId || membership.Version != write.MembershipVersion || membership.Role != write.ActingCapacity
            || RecordRetentionRules.Validate(policy) is not null || write.EventId == Guid.Empty || _eventIds.Contains(write.EventId)
            || string.IsNullOrWhiteSpace(write.CorrelationId) || write.CorrelationId.Length > 256 || write.CorrelationId.Any(char.IsControl)
            || policy.CreatedAt != (current?.CreatedAt ?? policy.UpdatedAt) || policy.UpdatedAt < parent.UpdatedAt
            || policy.UpdatedAt < membership.UpdatedAt || policy.UpdatedAt < current?.UpdatedAt
            || policy.UpdatedAt.Ticks % 10 != 0 || policy.CreatedAt.Ticks % 10 != 0
            || current is not null && (current.RecordClass != policy.RecordClass || current.Category != policy.Category || current.Jurisdiction != policy.Jurisdiction)
            || receipt.ActorId != write.ActorId || receipt.Key == Guid.Empty || receipt.Result != policy
            || policy.UpdatedAt > DateTimeOffset.MaxValue.AddHours(-24) || receipt.ExpiresAt != policy.UpdatedAt.AddHours(24)
            || receipt.Fingerprint.Length != 64 || !receipt.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')
            || string.IsNullOrWhiteSpace(subject.DisplayName) || subject.DisplayName.Length > 160 || subject.DisplayName.Any(char.IsControl))
            return RetentionPolicyWriteResult.SourceUnavailable;
        var ns = (policy.OrganizationId, policy.RecordClass, policy.Category.ToUpperInvariant(), policy.Jurisdiction?.Trim().ToUpperInvariant() ?? "");
        if (_namespaces.TryGetValue(ns, out var owner) && owner != policy.Id) return RetentionPolicyWriteResult.NamespaceConflict;
        if (work is not InMemoryWorkManagementStore audits)
            throw new InvalidOperationException("Demo retention audit persistence is unavailable.");
        await audits.AppendAuditFactAsync(write.EventId, policy.OrganizationId, write.ActorId, "RECORD_RETENTION_POLICY_CHANGED",
            "RECORD_RETENTION_POLICY", policy.Id, write.CorrelationId, policy.UpdatedAt, ct,
            new(policy.Version, policy.RecordClass == RetentionRecordClass.OperationalWork ? "OPERATIONAL_WORK" : policy.RecordClass.ToString().ToUpperInvariant(),
                policy.Action.ToString().ToUpperInvariant(), write.ActingCapacity == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN"));
        ct.ThrowIfCancellationRequested();
        _current[(policy.OrganizationId, policy.Id)] = policy;
        _history.Add((policy.OrganizationId, policy.Id, policy.Version), new(write, subject.DisplayName));
        _receipts.Add((policy.OrganizationId, write.ActorId, receipt.Key), receipt);
        _namespaces[ns] = policy.Id; _eventIds.Add(write.EventId);
        return RetentionPolicyWriteResult.Written;
    }

    public Action CaptureRollback()
    {
        Action[] restore = [DemoRollback.Dictionary(_current), DemoRollback.Dictionary(_history),
            DemoRollback.Dictionary(_receipts), DemoRollback.Dictionary(_namespaces), DemoRollback.Set(_eventIds)];
        return () => { foreach (var undo in restore) undo(); };
    }

    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !scope.OwnsOrganizationCommand(organization))
            throw new InvalidOperationException("Retention policy requires an owning transaction.");
    }
}
