using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Identity;

internal sealed class InMemoryIdentityUnitOfWork(ICommandActorAuthorization actors, IdentityRevocationReplayExecutor revocations,
    IAccountDeactivationOwnership ownership, InMemoryAccountOrganizationGate gate,
    DemoIdentityTransactionScope scope, IEnumerable<IDemoIdentityTransactionParticipant> participants,
    IEnumerable<IDemoWorkTransactionParticipant> workParticipants) : IIdentityUnitOfWork
{
    private readonly SemaphoreSlim _gate = gate.Commands;
    public Task<IdentityOperation<bool>> ExecuteDeactivationAsync(Guid actorId,
        Func<Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default, string correlationId = "")
        => ExecuteOwnedAsync(actorId, async () =>
        {
            if (!await actors.VerifyAsync(actorId, cancellationToken)) return IdentityOperation<bool>.Failure("session_unavailable");
            var plan = await ownership.PrepareAsync(actorId, cancellationToken);
            var error = await ownership.CheckAsync(plan, cancellationToken);
            if (error is not null) return IdentityOperation<bool>.Failure(error);
            var result = await operation();
            if (result.Succeeded) await ownership.CleanupAssignmentsAsync(plan, correlationId, cancellationToken);
            return result;
        }, cancellationToken, includeWork: true);

    public Task<IdentityOperation<bool>> ExecuteRevocationAsync(Guid expectedActor, string sessionHash, Guid key,
        IdentityRevocationKind kind, string correlationId, Func<Guid, Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default)
        => ExecuteOwnedAsync(null, () => revocations.ExecuteAsync(expectedActor, sessionHash, key, kind, async actor =>
        {
            if (kind != IdentityRevocationKind.Deactivate) return await operation(actor);
            var plan = await ownership.PrepareAsync(actor, cancellationToken);
            var error = await ownership.CheckAsync(plan, cancellationToken);
            if (error is not null) return IdentityOperation<bool>.Failure(error);
            var result = await operation(actor);
            if (result.Succeeded) await ownership.CleanupAssignmentsAsync(plan, correlationId, cancellationToken);
            return result;
        }, cancellationToken), cancellationToken, includeWork: kind == IdentityRevocationKind.Deactivate);

    public async Task<T> ExecuteRecoveryRequestAsync<T>(Func<Task<T>> operation, T neutralResult,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteOwnedAsync(null, async () =>
            IdentityOperation<T>.Success(await operation()), cancellationToken);
        return result.Value!;
    }

    public Task<IdentityOperation<UserProfile>> ExecuteTokenProofAsync(
        Func<Task<IdentityOperation<UserProfile>>> operation, CancellationToken cancellationToken = default)
        => ExecuteOwnedAsync(null, operation, cancellationToken);

    public Task<IdentityOperation<RegistrationOutcome>> ExecuteRegistrationAsync(
        Func<Task<IdentityOperation<RegistrationOutcome>>> operation, CancellationToken cancellationToken = default)
        => ExecuteOwnedAsync(null, operation, cancellationToken);

    public Task<IdentityOperation<LoginOutcome>> ExecuteSignInAsync(
        Func<Task<IdentityOperation<LoginOutcome>>> operation, CancellationToken cancellationToken = default)
        => ExecuteOwnedAsync(null, operation, cancellationToken);

    public Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default)
        => ExecuteOwnedAsync(actorId, async () => await actors.VerifyAsync(actorId, cancellationToken)
            ? await operation() : IdentityOperation<T>.Failure("session_unavailable"), cancellationToken);

    // This boundary covers account/profile/handle commands, registration, recovery requests, sign-in and token consumption,
    // with registered global identity state. Deactivation additionally holds the Work
    // gate and snapshots assignment/event state until its receipt and cancellation fence.
    // Producers retain their operation-specific credential and neutral-response policies.
    private async Task<IdentityOperation<T>> ExecuteOwnedAsync<T>(Guid? actor,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken, bool includeWork = false)
    {
        if (scope.Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        await _gate.WaitAsync(cancellationToken);
        Action[] rollback = []; var committed = false; var workHeld = false;
        try
        {
            // Account/Organization -> Work is the shared lock order. Both gates must
            // remain held while restoring global snapshots after cleanup/receipt failure.
            if (includeWork) { await gate.WorkCommands.WaitAsync(cancellationToken); workHeld = true; }
            using var owning = scope.Enter(actor);
            rollback = participants.Select(participant => participant.CaptureRollback())
                .Concat(includeWork ? workParticipants.Select(participant => participant.CaptureRollback()) : []).ToArray();
            var result = await operation(); cancellationToken.ThrowIfCancellationRequested();
            committed = result.Succeeded; return result;
        }
        finally
        {
            try { if (!committed) for (var index = rollback.Length - 1; index >= 0; index--) rollback[index](); }
            finally { if (workHeld) gate.WorkCommands.Release(); _gate.Release(); }
        }
    }
}
