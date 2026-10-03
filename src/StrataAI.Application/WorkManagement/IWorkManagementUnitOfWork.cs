namespace StrataAI.Application.WorkManagement;

public interface IWorkManagementUnitOfWork
{
    Task<WorkOperation<T>> ExecuteReadAsync<T>(Guid organizationId, Guid? actorId, string scopeFailureCode,
        Func<Task<bool>> authorize, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken = default);

    Task<WorkOperation<T>> ExecuteAsync<T>(
        Guid organizationId,
        WorkCommand command,
        Func<T?, Task<bool>> authorizeReplay,
        Func<Task<WorkOperation<T>>> operation,
        CancellationToken cancellationToken = default);
}
