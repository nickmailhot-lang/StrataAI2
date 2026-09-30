namespace StrataAI.Application.WorkManagement;

public interface IWorkManagementUnitOfWork
{
    Task<WorkOperation<T>> ExecuteAsync<T>(
        Guid organizationId,
        Func<Task<WorkOperation<T>>> operation,
        CancellationToken cancellationToken = default);
}
