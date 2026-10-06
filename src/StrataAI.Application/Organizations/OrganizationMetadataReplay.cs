using System.Collections.ObjectModel;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Organizations;

public sealed record OrganizationMetadataCursorBinding(Guid OrganizationId, Guid ActorId, Guid MembershipId, long MembershipVersion);
public interface IOrganizationMetadataCursorCodec
{
    string Encode(OrganizationMetadataCursorBinding binding, long position);
    bool TryDecode(OrganizationMetadataCursorBinding binding, string token, out long position);
}
public sealed record OrganizationMetadataEvent(Guid EventId, string EventType, Guid ActorId, Guid OrganizationId,
    long Version, DateTimeOffset CreatedAt, string EntityType, Guid EntityId)
{
    public OrganizationMetadataEvent(Guid eventId, string eventType, Guid actorId, Guid organizationId,
        long version, DateTimeOffset createdAt) : this(eventId, eventType, actorId, organizationId,
            version, createdAt, "Organization", organizationId) { }
    public Guid? BoardId => null;
    public IReadOnlyDictionary<string, string> Metadata => EmptyMetadata;
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
public sealed record OrganizationMetadataEventCandidate(long Sequence, OrganizationMetadataEvent Event, bool Ready);
public sealed record OrganizationMetadataEventWindow(long Position, bool HasMore, bool Pending, bool ResetRequired,
    IReadOnlyList<OrganizationMetadataEventCandidate> Events)
{
    public static OrganizationMetadataEventWindow Build(long since, long head, int limit, IReadOnlyList<OrganizationMetadataEventCandidate> rows)
    {
        if (since < 0 || head < 0 || limit is < 1 or > 100 || rows.Count > limit + 1)
            throw new ArgumentOutOfRangeException(nameof(since));
        if (since > head) return new(0, false, false, true, []);
        var position = since;
        List<OrganizationMetadataEventCandidate> events = [];
        foreach (var row in rows)
        {
            if (position == long.MaxValue || row.Sequence != position + 1 || row.Sequence > head)
                return new(0, false, false, true, []);
            if (!row.Ready) return new(position, false, true, false, events);
            if (events.Count == limit) return new(position, true, false, false, events);
            events.Add(row); position = row.Sequence;
        }
        // Metadata has no hidden audience gaps: missing history is a reset,
        // never permission to silently advance over a pending or absent event.
        return position < head ? new(0, false, false, true, []) : new(position, false, false, false, events);
    }
}
public interface IOrganizationMetadataEventReader
{
    Task<OrganizationMetadataCursorBinding?> GetScopeAsync(Guid organizationId, Guid actorId, CancellationToken cancellationToken);
    Task<long> GetHeadAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<OrganizationMetadataEventWindow> ReadAsync(Guid organizationId, long since, int limit, CancellationToken cancellationToken);
}
public sealed record OrganizationMetadataSyncPage(string Cursor, bool HasMore, bool Pending, bool ResetRequired,
    IReadOnlyList<OrganizationMetadataEvent> Events);

// Coordinator requires its owning transaction wrapper before transport use.
public sealed class OrganizationMetadataSynchronizationService(IOrganizationMetadataEventReader reader, IOrganizationMetadataCursorCodec cursors)
{
    public async Task<WorkOperation<bool>> IsCursorCurrentAsync(Guid organizationId, Guid actorId, string cursor,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty)
            return WorkOperation<bool>.Failure("organization_not_found");
        var scope = await reader.GetScopeAsync(organizationId, actorId, cancellationToken);
        if (scope is null || scope.OrganizationId != organizationId || scope.ActorId != actorId)
            return WorkOperation<bool>.Failure("organization_not_found");
        return WorkOperation<bool>.Success(scope.MembershipId != Guid.Empty && scope.MembershipVersion > 0
            && cursors.TryDecode(scope, cursor, out _));
    }
    public async Task<WorkOperation<OrganizationMetadataSyncPage>> ReadAsync(Guid organizationId, Guid actorId,
        string? cursor, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty) return WorkOperation<OrganizationMetadataSyncPage>.Failure("organization_not_found");
        if (limit is < 1 or > 100) return WorkOperation<OrganizationMetadataSyncPage>.Failure("invalid_sync_limit");
        var scope = await reader.GetScopeAsync(organizationId, actorId, cancellationToken);
        if (scope is null || scope.OrganizationId != organizationId || scope.ActorId != actorId)
            return WorkOperation<OrganizationMetadataSyncPage>.Failure("organization_not_found");
        if (scope.MembershipId == Guid.Empty || scope.MembershipVersion < 1)
            return WorkOperation<OrganizationMetadataSyncPage>.Failure("organization_sync_unavailable");
        OrganizationMetadataEventWindow page;
        if (cursor is null || !cursors.TryDecode(scope, cursor, out var position))
            page = new(await reader.GetHeadAsync(organizationId, cancellationToken), false, false, true, []);
        else
        {
            page = await reader.ReadAsync(organizationId, position, limit, cancellationToken);
            if (page.ResetRequired) page = new(await reader.GetHeadAsync(organizationId, cancellationToken), false, false, true, []);
        }
        var current = await reader.GetScopeAsync(organizationId, actorId, cancellationToken);
        if (current is null) return WorkOperation<OrganizationMetadataSyncPage>.Failure("organization_not_found");
        if (current != scope || page.Position < 0 || page.Events.Count > limit || page.Events.Any(e => !e.Ready
            || e.Event.EventId == Guid.Empty || e.Event.ActorId == Guid.Empty || e.Event.OrganizationId != organizationId
            || e.Event.Version < 1 || e.Event.EntityId == Guid.Empty
            || e.Event.EventType is not ("ORGANIZATION_CREATED" or "ORGANIZATION_UPDATED" or "ORGANIZATION_MEMBER_ADDED" or "ORGANIZATION_MEMBER_REMOVED" or "ORGANIZATION_MEMBER_INVITED" or "INVITATION_REVOKED" or "INVITATION_ACCEPTED")
            || (e.Event.EventType is "ORGANIZATION_MEMBER_INVITED" or "INVITATION_REVOKED" or "INVITATION_ACCEPTED" ? e.Event.EntityType != "Invitation"
              : e.Event.EventType is "ORGANIZATION_MEMBER_ADDED" or "ORGANIZATION_MEMBER_REMOVED"
                ? e.Event.EntityType != "OrganizationMembership"
                : e.Event.EntityType != "Organization" || e.Event.EntityId != organizationId)
            || e.Event.EventType is "ORGANIZATION_CREATED" or "ORGANIZATION_MEMBER_INVITED" && e.Event.Version != 1
            || e.Event.EventType is "ORGANIZATION_UPDATED" or "ORGANIZATION_MEMBER_REMOVED" or "INVITATION_REVOKED" or "INVITATION_ACCEPTED" && e.Event.Version <= 1))
            return WorkOperation<OrganizationMetadataSyncPage>.Failure("organization_sync_unavailable");
        return WorkOperation<OrganizationMetadataSyncPage>.Success(new(cursors.Encode(current, page.Position), page.HasMore, page.Pending,
            page.ResetRequired, page.Events.Select(e => e.Event).ToArray()));
    }
}

public sealed class TransactionalOrganizationMetadataSynchronization(OrganizationMetadataSynchronizationService replay,
    IWorkManagementUnitOfWork transactions, IWorkManagementStore work, IOrganizationStore organizations)
{
    public Task<WorkOperation<bool>> IsCursorCurrentAsync(Guid organizationId, Guid actorId, string cursor,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty)
            return Task.FromResult(WorkOperation<bool>.Failure("organization_not_found"));
        return transactions.ExecuteReadAsync(organizationId, actorId, "organization_not_found", async () =>
        {
            if (!await work.AcquireOrganizationReadScopeAsync(organizationId, actorId, cancellationToken)) return false;
            return await organizations.FindOrganizationAsync(organizationId, cancellationToken) is { Status: OrganizationStatus.Active }
                && await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken) is { Active: true };
        }, () => replay.IsCursorCurrentAsync(organizationId, actorId, cursor, cancellationToken), cancellationToken);
    }
    public Task<WorkOperation<OrganizationMetadataSyncPage>> ReadAsync(Guid organizationId, Guid actorId,
        string? cursor, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty)
            return Task.FromResult(WorkOperation<OrganizationMetadataSyncPage>.Failure("organization_not_found"));
        return transactions.ExecuteReadAsync(organizationId, actorId, "organization_not_found", async () =>
        {
            if (!await work.AcquireOrganizationReadScopeAsync(organizationId, actorId, cancellationToken)) return false;
            return await organizations.FindOrganizationAsync(organizationId, cancellationToken) is { Status: OrganizationStatus.Active }
                && await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken) is { Active: true };
        }, () => replay.ReadAsync(organizationId, actorId, cursor, limit, cancellationToken), cancellationToken);
    }
}
