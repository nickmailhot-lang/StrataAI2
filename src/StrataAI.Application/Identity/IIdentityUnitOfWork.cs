namespace StrataAI.Application.Identity;

public interface IIdentityUnitOfWork
{
    Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default);
}
