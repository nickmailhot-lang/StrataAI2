namespace StrataAI.Application.Identity;

public interface IIdentityUnitOfWork
{
    // Password verification occurs inside this boundary; it does not require an existing session.
    Task<IdentityOperation<LoginOutcome>> ExecuteSignInAsync(
        Func<Task<IdentityOperation<LoginOutcome>>> operation, CancellationToken cancellationToken = default);

    Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default);
}
