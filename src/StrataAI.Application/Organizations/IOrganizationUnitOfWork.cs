namespace StrataAI.Application.Organizations;

public interface IOrganizationUnitOfWork
{
    // Deletion recovery admits retained DELETING/DELETED parents only for
    // request acknowledgment recovery; normal commands keep active admission.
    Task<OrganizationOperation<T>> ExecuteAsync<T>(
        Guid organizationId, Guid actorUserId, Guid? targetUserId, bool creating,
        Func<Task<OrganizationOperation<T>>> operation,
        CancellationToken cancellationToken = default, bool allowDeletionRecovery = false);
}
