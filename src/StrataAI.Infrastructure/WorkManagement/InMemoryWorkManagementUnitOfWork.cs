using System.Collections.Concurrent;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo has no PostgreSQL dependency. Results live only for this host lifetime.
internal sealed class InMemoryWorkManagementUnitOfWork(IClock clock, ICommandActorAuthorization actors,
    DemoWorkTransactionScope scope, IEnumerable<IDemoWorkTransactionParticipant> participants) : IWorkManagementUnitOfWork
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<(Guid Organization, Guid Actor, Guid Key), (string Fingerprint, DateTimeOffset Expires, object Result)> _results = new();

    public async Task<WorkOperation<T>> ExecuteReadAsync<T>(Guid organizationId, Guid? actorId, string scopeFailureCode,
        Func<Task<bool>> authorize, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        using var owned = scope.Enter(organizationId);
        Action[] rollback = []; var committed = false;
        try
        {
            rollback = participants.Select(participant => participant.CaptureRollback()).ToArray();
            if (!await authorize()) return WorkOperation<T>.Failure(scopeFailureCode);
            if (actorId is { } actor && !await actors.VerifyAsync(actor, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
            var result = await operation();
            if (!result.Succeeded) return result;
            if (!await authorize()) return WorkOperation<T>.Failure(scopeFailureCode);
            if (actorId is { } current && !await actors.VerifyAsync(current, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
            cancellationToken.ThrowIfCancellationRequested();
            committed = true; return result;
        }
        finally { try { if (!committed) foreach (var restore in rollback.Reverse()) restore(); } finally { _gate.Release(); } }
    }

    public async Task<WorkOperation<T>> ExecuteAsync<T>(Guid organizationId, WorkCommand command,
        Func<T?, Task<bool>> authorizeReplay, Func<Task<WorkOperation<T>>> operation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        using var owned = scope.Enter(organizationId);
        Action[] rollback = []; var committed = false;
        try
        {
            rollback = participants.Select(participant => participant.CaptureRollback()).ToArray();
            if (!await authorizeReplay(default)) return WorkOperation<T>.Failure(command.ScopeFailureCode);
            if (!await actors.VerifyAsync(command.ActorId, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
            var key = (organizationId, command.ActorId, command.Key.GetValueOrDefault());
            if (command.Key is not null && _results.TryGetValue(key, out var previous))
            {
                if (previous.Fingerprint != command.Fingerprint) return WorkOperation<T>.Failure("idempotency_key_reused");
                if (previous.Expires <= clock.UtcNow) return WorkOperation<T>.Failure("idempotency_key_expired");
                var cached = (WorkOperation<T>)previous.Result;
                if (!await authorizeReplay(cached.Value)) return WorkOperation<T>.Failure(command.ScopeFailureCode);
                if (!await actors.VerifyAsync(command.ActorId, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
                cancellationToken.ThrowIfCancellationRequested();
                committed = true; return cached;
            }
            // Bound demo memory without evicting a key and allowing a duplicate.
            if (command.Key is not null && _results.Count >= 10000) return WorkOperation<T>.Failure("work_storage_unavailable");
            var result = await operation();
            if (!result.Succeeded) return result;
            if (!await authorizeReplay(result.Value)) return WorkOperation<T>.Failure(command.ScopeFailureCode);
            if (!await actors.VerifyAsync(command.ActorId, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
            cancellationToken.ThrowIfCancellationRequested();
            if (command.Key is not null) _results[key] = (command.Fingerprint, clock.UtcNow.AddHours(24), result);
            committed = true; return result;
        }
        finally { try { if (!committed) foreach (var restore in rollback.Reverse()) restore(); } finally { _gate.Release(); } }
    }
}
