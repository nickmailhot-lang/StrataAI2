using System.Collections.Concurrent;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo has no PostgreSQL dependency. Results live only for this host lifetime.
internal sealed class InMemoryWorkManagementUnitOfWork(IClock clock, ICommandActorAuthorization actors) : IWorkManagementUnitOfWork
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<(Guid Organization, Guid Actor, Guid Key), (string Fingerprint, DateTimeOffset Expires, object Result)> _results = new();

    public async Task<WorkOperation<T>> ExecuteAsync<T>(Guid organizationId, WorkCommand command,
        Func<T?, Task<bool>> authorizeReplay, Func<Task<WorkOperation<T>>> operation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await authorizeReplay(default)) return WorkOperation<T>.Failure(command.ScopeFailureCode);
            if (!await actors.VerifyAsync(command.ActorId, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
            if (command.Key is null) return await operation();
            var key = (organizationId, command.ActorId, command.Key.Value);
            if (_results.TryGetValue(key, out var previous))
            {
                if (previous.Fingerprint != command.Fingerprint) return WorkOperation<T>.Failure("idempotency_key_reused");
                if (previous.Expires <= clock.UtcNow) return WorkOperation<T>.Failure("idempotency_key_expired");
                var cached = (WorkOperation<T>)previous.Result;
                return await authorizeReplay(cached.Value) ? cached : WorkOperation<T>.Failure(command.ScopeFailureCode);
            }
            // Bound demo memory without evicting a key and allowing a duplicate.
            if (_results.Count >= 10000) return WorkOperation<T>.Failure("work_storage_unavailable");
            var result = await operation();
            if (result.Succeeded) _results[key] = (command.Fingerprint, clock.UtcNow.AddHours(24), result);
            return result;
        }
        finally { _gate.Release(); }
    }
}
