using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed class InMemoryIdentityUnitOfWork(ICommandActorAuthorization actors) : IIdentityUnitOfWork
{
    private readonly SemaphoreSlim _gate = new(1, 1);
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
