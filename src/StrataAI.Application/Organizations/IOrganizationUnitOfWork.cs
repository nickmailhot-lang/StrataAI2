namespace StrataAI.Application.Organizations;

public interface IOrganizationUnitOfWork
{
    Task<OrganizationOperation<T>> ExecuteAsync<T>(
        Guid organizationId, Guid actorUserId, Guid? targetUserId, bool creating,
        Func<Task<OrganizationOperation<T>>> operation,
        CancellationToken cancellationToken = default, bool allowDeletionRecovery = false);
}
