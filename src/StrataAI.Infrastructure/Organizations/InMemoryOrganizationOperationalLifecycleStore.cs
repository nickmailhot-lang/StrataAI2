using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationOperationalLifecycleStore(InMemoryOrganizationStore organizations,
    IIdentityStore identities, ICommandActorAuthorization actors, IClock clock, InMemoryWorkManagementStore audits,
    DemoWorkTransactionScope workScope, OrganizationOperationalLifecycleCommandScope lifecycle)
    : IOrganizationOperationalLifecycleStore, IOrganizationOperationalLifecycleEventReader, IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid Organization, long Version), OrganizationOperationalLifecycleTransition> _sources = [];
    private readonly Dictionary<(Guid Organization, Guid Actor, Guid Key), OrganizationOperationalLifecycleReceipt> _receipts = [];
    private readonly HashSet<Guid> _eventIds = [];
    private readonly Dictionary<(Guid Organization, long Version), DateTimeOffset> _publications = [];

    public async Task<OrganizationOperationalLifecycleScope?> ReadScopeAsync(Guid organization, Guid actor, CancellationToken ct)
    {
        Require(organization, actor, ct);
        var parent = await organizations.FindOrganizationAsync(organization, ct);
        var member = await organizations.FindMembershipAsync(organization, actor, ct);
        var account = await identities.FindUserByIdAsync(actor, ct);
        if (parent is null || member is null || account?.Id != actor || account.Status != AccountStatus.Active) return null;
        var caption = account.DisplayName;
        if (string.IsNullOrWhiteSpace(caption) || caption.Length > 160 || caption.Any(char.IsControl)) caption = "Member " + actor;
        return new(organization, parent.Status, parent.Version, parent.UpdatedAt, actor, caption,
            member.Id, member.Version, member.Role, member.Active, parent.Name);
    }

    public Task<OrganizationOperationalLifecycleReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        Require(organization, actor, ct); return Task.FromResult(_receipts.GetValueOrDefault((organization, actor, key)));
    }

    public async Task<OrganizationOperationalLifecycleWriteResult> WriteAsync(long expectedVersion,
        OrganizationOperationalLifecycleTransition source, OrganizationOperationalLifecycleReceipt receipt, CancellationToken ct)
    {
        Require(source.OrganizationId, source.ActorId, ct);
        var scope = await ReadScopeAsync(source.OrganizationId, source.ActorId, ct);
        if (scope is null || scope.Version != expectedVersion) return OrganizationOperationalLifecycleWriteResult.VersionConflict;
        if (_receipts.ContainsKey((source.OrganizationId, receipt.ActorId, receipt.Key))) return OrganizationOperationalLifecycleWriteResult.KeyConflict;
        if (!await actors.VerifyAsync(source.ActorId, ct) || !OrganizationOperationalLifecycleRules.ValidWrite(scope, expectedVersion,
            source, receipt, clock.UtcNow) || _eventIds.Contains(source.EventId) || _sources.ContainsKey((source.OrganizationId, source.Version)))
            return OrganizationOperationalLifecycleWriteResult.InvalidSource;
        if (!organizations.ChangeOperationalStatus(expectedVersion, source, lifecycle)) return OrganizationOperationalLifecycleWriteResult.VersionConflict;
        await audits.AppendAuditFactAsync(source.EventId, source.OrganizationId, source.ActorId, source.EventType,
            source.EntityType, source.EntityId, source.CorrelationId, source.CreatedAt, ct);
        ct.ThrowIfCancellationRequested();
        _sources.Add((source.OrganizationId, source.Version), source);
        _receipts.Add((source.OrganizationId, receipt.ActorId, receipt.Key), receipt);
        _eventIds.Add(source.EventId);
        // Demo simulates publication locally, under the same rollback gate.
        // Production readiness requires a live Worker lease instead.
        _publications.Add((source.OrganizationId, source.Version), clock.UtcNow < source.CreatedAt ? source.CreatedAt : clock.UtcNow.ToUniversalTime());
        return OrganizationOperationalLifecycleWriteResult.Written;
    }

    public Task<OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>> ReadAsync(
        Guid organization, Guid actor, long afterVersion, CancellationToken ct)
    {
        Require(organization, actor, ct);
        IReadOnlyList<OrganizationOperationalLifecyclePublication> rows = _sources
            .Where(row => row.Key.Organization == organization && row.Key.Version > afterVersion)
            .OrderBy(row => row.Key.Version).Take(51)
            .Select(row => new OrganizationOperationalLifecyclePublication(row.Value,
                _publications.TryGetValue(row.Key, out var ready) ? ready : null)).ToArray();
        return Task.FromResult(OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>.Success(rows));
    }

    public Action CaptureRollback()
    {
        Action[] restore = [DemoRollback.Dictionary(_sources), DemoRollback.Dictionary(_receipts), DemoRollback.Set(_eventIds), DemoRollback.Dictionary(_publications)];
        return () => { foreach (var action in restore) action(); };
    }

    private void Require(Guid organization, Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!lifecycle.Owns(organization, actor) || !workScope.OwnsOrganizationCommand(organization))
            throw new InvalidOperationException("An owning lifecycle transaction is required.");
    }
}
