namespace StrataAI.Application.Identity;

public interface IIdentityUnitOfWork
{
    Task<IdentityOperation<bool>> ExecuteDeactivationAsync(Guid actorId,
        Func<Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default, string correlationId = "");
    Task<IdentityOperation<bool>> ExecuteRevocationAsync(Guid expectedActor, string sessionHash, Guid key,
        IdentityRevocationKind kind, string correlationId, Func<Guid, Task<IdentityOperation<bool>>> operation,
        CancellationToken cancellationToken = default);
    // Public recovery acknowledgments must remain indistinguishable for unknown accounts and failed storage.
    Task<T> ExecuteRecoveryRequestAsync<T>(Func<Task<T>> operation, T neutralResult,
        CancellationToken cancellationToken = default);

    // The operation verifies an expiring single-use token under the account lock.
    Task<IdentityOperation<UserProfile>> ExecuteTokenProofAsync(
        Func<Task<IdentityOperation<UserProfile>>> operation, CancellationToken cancellationToken = default);

    // Self-registration policy and validation are enforced by the operation inside this boundary.
    Task<IdentityOperation<RegistrationOutcome>> ExecuteRegistrationAsync(
        Func<Task<IdentityOperation<RegistrationOutcome>>> operation, CancellationToken cancellationToken = default);

    // Password verification occurs inside this boundary; it does not require an existing session.
    Task<IdentityOperation<LoginOutcome>> ExecuteSignInAsync(
        Func<Task<IdentityOperation<LoginOutcome>>> operation, CancellationToken cancellationToken = default);

    // Private interaction history writes hold shared account admission, after
    // the target Organization parent, rather than an exclusive profile lock.
    Task<IdentityOperation<T>> ExecuteObservationAsync<T>(Guid actorId, Guid? organizationId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default);

    Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default);
}
