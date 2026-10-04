using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class InMemoryIdentityUnitOfWork(ICommandActorAuthorization actors, IdentityRevocationReplayExecutor revocations,
    IAccountDeactivationOwnership ownership, InMemoryAccountOrganizationGate gate,
    DemoIdentityTransactionScope scope, IEnumerable<IDemoIdentityTransactionParticipant> participants) : IIdentityUnitOfWork
{
    private readonly SemaphoreSlim _gate = gate.Commands;
    public async Task<IdentityOperation<bool>> ExecuteDeactivationAsync(Guid actorId,
        Func<Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default, string correlationId = "")
    {
        if (scope.Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await actors.VerifyAsync(actorId, cancellationToken)) return IdentityOperation<bool>.Failure("session_unavailable");
            var plan = await ownership.PrepareAsync(actorId, cancellationToken);
            var error = await ownership.CheckAsync(plan, cancellationToken);
            if (error is not null) return IdentityOperation<bool>.Failure(error);
            var result = await operation();
            if (result.Succeeded) await ownership.CleanupAssignmentsAsync(plan, correlationId, cancellationToken);
            return result;
        }
        finally { _gate.Release(); }
    }
    public async Task<IdentityOperation<bool>> ExecuteRevocationAsync(Guid expectedActor, string sessionHash, Guid key,
        IdentityRevocationKind kind, string correlationId, Func<Guid, Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default)
    {
        if (scope.Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await revocations.ExecuteAsync(expectedActor, sessionHash, key, kind, async actor =>
            {
                if (kind != IdentityRevocationKind.Deactivate) return await operation(actor);
                var plan = await ownership.PrepareAsync(actor, cancellationToken);
                var error = await ownership.CheckAsync(plan, cancellationToken);
                if (error is not null) return IdentityOperation<bool>.Failure(error);
                var result = await operation(actor);
                if (result.Succeeded) await ownership.CleanupAssignmentsAsync(plan, correlationId, cancellationToken);
                return result;
            }, cancellationToken);
        }
        finally { _gate.Release(); }
    }
    public async Task<T> ExecuteRecoveryRequestAsync<T>(Func<Task<T>> operation, T neutralResult,
        CancellationToken cancellationToken = default)
    {
        if (scope.Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        await _gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _gate.Release(); }
    }

    public async Task<IdentityOperation<UserProfile>> ExecuteTokenProofAsync(
        Func<Task<IdentityOperation<UserProfile>>> operation, CancellationToken cancellationToken = default)
    {
        if (scope.Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        await _gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _gate.Release(); }
    }

    public Task<IdentityOperation<RegistrationOutcome>> ExecuteRegistrationAsync(
        Func<Task<IdentityOperation<RegistrationOutcome>>> operation, CancellationToken cancellationToken = default)
        => ExecuteOwnedAsync(null, operation, cancellationToken);

    public async Task<IdentityOperation<LoginOutcome>> ExecuteSignInAsync(
        Func<Task<IdentityOperation<LoginOutcome>>> operation, CancellationToken cancellationToken = default)
    {
        if (scope.Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        await _gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _gate.Release(); }
    }

    public Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default)
        => ExecuteOwnedAsync(actorId, async () => await actors.VerifyAsync(actorId, cancellationToken)
            ? await operation() : IdentityOperation<T>.Failure("session_unavailable"), cancellationToken);

    // This boundary covers account/profile/handle commands and registration,
    // with registered global identity state. Cross-module lifecycle cleanup
    // and the other specialized boundaries still need their own rollback proof.
    private async Task<IdentityOperation<T>> ExecuteOwnedAsync<T>(Guid? actor,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken)
    {
        if (scope.Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        await _gate.WaitAsync(cancellationToken);
        Action[] rollback = []; var committed = false;
        try
        {
            using var owning = scope.Enter(actor);
            rollback = participants.Select(participant => participant.CaptureRollback()).ToArray();
            var result = await operation(); cancellationToken.ThrowIfCancellationRequested();
            committed = result.Succeeded; return result;
        }
        finally
        {
            try { if (!committed) for (var index = rollback.Length - 1; index >= 0; index--) rollback[index](); }
            finally { _gate.Release(); }
        }
    }
}
