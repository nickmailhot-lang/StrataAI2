using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed class InMemoryIdentityUnitOfWork(ICommandActorAuthorization actors, IdentityRevocationReplayExecutor revocations) : IIdentityUnitOfWork
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<IdentityOperation<bool>> ExecuteRevocationAsync(Guid expectedActor, string sessionHash, Guid key,
        IdentityRevocationKind kind, string correlationId, Func<Guid, Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await revocations.ExecuteAsync(expectedActor, sessionHash, key, kind, operation, cancellationToken); }
        finally { _gate.Release(); }
    }
    public async Task<T> ExecuteRecoveryRequestAsync<T>(Func<Task<T>> operation, T neutralResult,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _gate.Release(); }
    }

    public async Task<IdentityOperation<UserProfile>> ExecuteTokenProofAsync(
        Func<Task<IdentityOperation<UserProfile>>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _gate.Release(); }
    }

    public async Task<IdentityOperation<RegistrationOutcome>> ExecuteRegistrationAsync(
        Func<Task<IdentityOperation<RegistrationOutcome>>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _gate.Release(); }
    }

    public async Task<IdentityOperation<LoginOutcome>> ExecuteSignInAsync(
        Func<Task<IdentityOperation<LoginOutcome>>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _gate.Release(); }
    }

    public async Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await actors.VerifyAsync(actorId, cancellationToken)
                ? await operation() : IdentityOperation<T>.Failure("session_unavailable");
        }
        finally { _gate.Release(); }
    }
}
