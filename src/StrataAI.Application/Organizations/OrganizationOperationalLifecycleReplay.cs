using StrataAI.Application.Identity;

namespace StrataAI.Application.Organizations;

public sealed record OrganizationOperationalLifecyclePublication(OrganizationOperationalLifecycleTransition Source, DateTimeOffset? ReadyAt);
public interface IOrganizationOperationalLifecycleEventReader
{
    // Owning retained-parent transaction only; include unpublished sources so a
    // later ready event cannot advance a subscriber past a publication gap.
    Task<OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>> ReadAsync(
        Guid organization, Guid actor, long afterVersion, CancellationToken ct);
}
public sealed record OrganizationOperationalLifecycleEvent(Guid EventId, Guid OrganizationId, Guid ActorId,
    string EventType, string EntityType, Guid EntityId, long Version, OrganizationStatus PreviousStatus,
    OrganizationStatus Status, string ActingCapacity, DateTimeOffset CreatedAt);
public sealed record OrganizationOperationalLifecycleReplayPage(Guid OrganizationId, Guid ActorId,
    OrganizationStatus Status, long Version, long AfterVersion, long? NextVersion, bool Pending,
    IReadOnlyList<OrganizationOperationalLifecycleEvent> Events);

public sealed class OrganizationOperationalLifecycleReplayService(IOrganizationOperationalLifecycleUnitOfWork unit,
    IOrganizationOperationalLifecycleStore store, IOrganizationOperationalLifecycleEventReader events,
    ICommandActorAuthorization actors)
{
    public Task<OrganizationOperation<OrganizationOperationalLifecycleReplayPage>> ReadAsync(Guid organization,
        Guid actor, long afterVersion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || actor == Guid.Empty) return Task.FromResult(Failure("organization_not_found"));
        return unit.ExecuteAsync(organization, actor, async () =>
        {
        if (!await actors.VerifyAsync(actor, ct)) return Failure("session_unavailable");
        var scope = await store.ReadScopeAsync(organization, actor, ct);
        if (!Admitted(scope, organization, actor)) return Failure("organization_not_found");
        if (afterVersion < 0 || afterVersion > scope!.Version) return Failure("invalid_organization_cursor");
        var read = await events.ReadAsync(organization, actor, afterVersion, ct);
        if (!read.Succeeded) return Failure(read.ErrorCode ?? "organization_lifecycle_source_unavailable");
        var rows = read.Value;
        if (rows is null || rows.Count > 51) return Failure("organization_lifecycle_source_unavailable");
        long previous = afterVersion; var ids = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var source = row.Source;
            if (source is null || source.OrganizationId != organization || source.EventId == Guid.Empty
                || !ids.Add(source.EventId) || source.ActorId == Guid.Empty || source.Version <= previous || source.Version > scope.Version
                || source.PreviousStatus is not (OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated)
                || source.Status is not (OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated)
                || source.Status == source.PreviousStatus || source.ActingCapacity is not ("ORGANIZATION_OWNER" or "ORGANIZATION_ADMIN")
                || source.CreatedAt.Offset != TimeSpan.Zero || source.CreatedAt.Ticks % 10 != 0
                || source.CreatedAt > scope.UpdatedAt || row.ReadyAt is { } ready && (ready.Offset != TimeSpan.Zero || ready < source.CreatedAt))
                return Failure("organization_lifecycle_source_unavailable");
            previous = source.Version;
        }
        var result = new List<OrganizationOperationalLifecycleEvent>(); var pending = false;
        foreach (var row in rows)
        {
            if (row.ReadyAt is null) { pending = true; break; }
            if (result.Count == 50) break;
            var source = row.Source;
            result.Add(new(source.EventId, source.OrganizationId, source.ActorId, source.EventType, source.EntityType,
                source.EntityId, source.Version, source.PreviousStatus, source.Status, source.ActingCapacity, source.CreatedAt));
        }
        if (!await actors.VerifyAsync(actor, ct)) return Failure("session_unavailable");
        var current = await store.ReadScopeAsync(organization, actor, ct);
        if (!Admitted(current, organization, actor) || current!.MembershipId != scope.MembershipId
            || current.MembershipVersion != scope.MembershipVersion || current.Role != scope.Role) return Failure("organization_not_found");
        if (current.Status != scope.Status || current.Version != scope.Version || current.UpdatedAt != scope.UpdatedAt)
            return Failure("version_conflict");
        if (!await actors.VerifyAsync(actor, ct)) return Failure("session_unavailable");
        ct.ThrowIfCancellationRequested();
        return OrganizationOperation<OrganizationOperationalLifecycleReplayPage>.Success(new(organization, actor,
            current.Status, current.Version, afterVersion, result.Count > 0 && rows.Count > result.Count ? result[^1].Version : null,
            pending, result));
        }, ct);
    }

    private static bool Admitted(OrganizationOperationalLifecycleScope? scope, Guid organization, Guid actor) =>
        organization != Guid.Empty && actor != Guid.Empty && scope is not null && scope.OrganizationId == organization
        && scope.ActorId == actor && scope.Status is OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated
        && scope.Version > 0 && scope.MembershipId != Guid.Empty && scope.MembershipVersion > 0 && scope.MembershipActive
        && Enum.IsDefined(scope.Role);
    private static OrganizationOperation<OrganizationOperationalLifecycleReplayPage> Failure(string code) =>
        OrganizationOperation<OrganizationOperationalLifecycleReplayPage>.Failure(code);
}
