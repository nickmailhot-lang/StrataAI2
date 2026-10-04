using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<ArchivedBoardPage>> ListArchivedBoardsAsync(Guid organizationId, Guid actorId,
        Guid? after = null, CancellationToken cancellationToken = default)
    {
        var parent = await organizationStore.FindOrganizationAsync(organizationId, cancellationToken);
        var member = await organizationStore.FindMembershipAsync(organizationId, actorId, cancellationToken);
        if (parent is not { Status: OrganizationStatus.Active } || member is not { Active: true })
            return WorkOperation<ArchivedBoardPage>.Failure("organization_not_found");
        if (after == Guid.Empty) return WorkOperation<ArchivedBoardPage>.Failure("invalid_archive_cursor");
        var rows = await store.ListArchivedBoardsAsync(organizationId, actorId,
            member.Role is OrganizationRole.Owner or OrganizationRole.Admin, after, cancellationToken);
        var items = rows.Take(50).ToArray();
        return WorkOperation<ArchivedBoardPage>.Success(new(organizationId, items, rows.Count > 50 ? items[^1].Id : null));
    }
}

public sealed partial class TransactionalWorkManagementService
{
    public Task<WorkOperation<ArchivedBoardPage>> ListArchivedBoardsAsync(Guid organizationId, Guid actorId,
        Guid? after = null, CancellationToken cancellationToken = default) =>
        transactions.ExecuteReadAsync(organizationId, actorId, "organization_not_found", async () =>
        {
            if (!await store.AcquireOrganizationReadScopeAsync(organizationId, actorId, cancellationToken)) return false;
            return await organizations.FindOrganizationAsync(organizationId, cancellationToken) is { Status: OrganizationStatus.Active } &&
                await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken) is { Active: true };
        }, () => inner.ListArchivedBoardsAsync(organizationId, actorId, after, cancellationToken), cancellationToken);
}
