using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkEventStore
{
    public async Task<OrganizationBoardCursorBinding?> GetScopeAsync(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default)
    {
        RequireOrganizationReplayScope(organizationId);
        var parent = await organizations.FindOrganizationAsync(organizationId, cancellationToken);
        var membership = await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken);
        if (parent is not { Status: OrganizationStatus.Active } || membership is not { Active: true }) return null;
        var grants = ((InMemoryWorkManagementStore)work).DirectoryPermissionRevision(organizationId, actorId);
        // Actual membership version covers role/withdrawal/restoration. Actual
        // Board grant revisions cover audience changes on other Boards. Each is
        // actor-specific; no content hash or another person's counter is used.
        return new(organizationId, actorId, membership.Id, parent.Version, membership.Id, checked(membership.Version + grants));
    }

    public Task<long> GetHeadAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        RequireOrganizationReplayScope(organizationId); cancellationToken.ThrowIfCancellationRequested();
        lock (_events) return Task.FromResult(_organizationStreams.GetValueOrDefault(organizationId));
    }

    async Task<WorkOperation<OrganizationBoardEventPage>> IOrganizationBoardEventReader.ReadAsync(Guid organizationId,
        Guid actorId, long since, int limit, CancellationToken cancellationToken)
    {
        RequireOrganizationReplayScope(organizationId);
        if (since < 0 || limit is < 1 or > 100)
            return WorkOperation<OrganizationBoardEventPage>.Failure("invalid_sync_cursor");
        if (await GetScopeAsync(organizationId, actorId, cancellationToken) is null)
            return WorkOperation<OrganizationBoardEventPage>.Failure("organization_not_found");
        var member = await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken);
        var audience = ((InMemoryWorkManagementStore)work).AdministeredBoardIds(organizationId, actorId,
            member?.Role is OrganizationRole.Owner or OrganizationRole.Admin);
        lock (_events)
        {
            var head = _organizationStreams.GetValueOrDefault(organizationId);
            var rows = _organizationEvents.Where(pair => pair.Key.Organization == organizationId)
                .Select(pair => pair.Value).Where(row => row.Sequence > since && row.Sequence <= head && audience.Contains(row.BoardId))
                .OrderBy(row => row.Sequence).Take(limit + 1).ToArray();
            return WorkOperation<OrganizationBoardEventPage>.Success(OrganizationBoardEventWindow.Build(since, head, limit, rows));
        }
    }
    private void RequireOrganizationReplayScope(Guid organizationId)
    {
        if (organizationId == Guid.Empty || !scope.Owns(organizationId))
            throw new InvalidOperationException("Organization Board replay requires the owning Work transaction.");
    }
}
