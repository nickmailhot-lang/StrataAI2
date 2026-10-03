using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresWorkManagementUnitOfWork(
    PostgresConnectionFactory connections,
    ICommandActorAuthorization actors,
    ILogger<PostgresWorkManagementUnitOfWork> logger) : IWorkManagementUnitOfWork
{
    public async Task<WorkOperation<T>> ExecuteReadAsync<T>(Guid organizationId, Guid? actorId, string scopeFailureCode,
        Func<Task<bool>> authorize, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            // Scoped read callbacks share parent locks and the tenant transaction.
            // Anonymous reads have no command receipt or fabricated session actor.
            return await connections.ExecuteTenantCommandAsync(organizationId, async () =>
            {
                if (!await authorize()) return WorkOperation<T>.Failure(scopeFailureCode);
                if (actorId is { } actor && !await actors.VerifyAsync(actor, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
                var result = await operation();
                if (!result.Succeeded) return result;
                if (!await authorize()) return WorkOperation<T>.Failure(scopeFailureCode);
                if (actorId is { } current && !await actors.VerifyAsync(current, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
                return result;
            }, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Work read lacked a database acknowledgment for Organization {OrganizationId}; database code {DatabaseCode}.",
                organizationId, exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return WorkOperation<T>.Failure("work_storage_unavailable");
        }
    }

    public async Task<WorkOperation<T>> ExecuteAsync<T>(Guid organizationId, WorkCommand command,
        Func<T?, Task<bool>> authorizeReplay, Func<Task<WorkOperation<T>>> operation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteTenantCommandAsync(organizationId, async () =>
            {
                // Never disclose a previously authorized response to a revoked actor.
                if (!await authorizeReplay(default)) return WorkOperation<T>.Failure(command.ScopeFailureCode);
                if (!await actors.VerifyAsync(command.ActorId, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
                if (command.Key is null) return await operation();
                await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
                NpgsqlCommand Query(string sql)
                {
                    var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
                    query.Parameters.AddWithValue("tenant", organizationId);
                    query.Parameters.AddWithValue("actor", command.ActorId);
                    query.Parameters.AddWithValue("key", command.Key.Value);
                    return query;
                }
                await using (var claim = Query("""
                    INSERT INTO work_command_replays(tenant_id,actor_id,key_id,fingerprint)
                    VALUES (@tenant,@actor,@key,@fingerprint) ON CONFLICT DO NOTHING;
                    """))
                {
                    claim.Parameters.AddWithValue("fingerprint", command.Fingerprint);
                    await claim.ExecuteNonQueryAsync(cancellationToken);
                }
                // A concurrent duplicate waits for the original transaction's commit
                // or rollback at the unique claim, then reads the authoritative result.
                string? resultJson;
                await using (var lookup = Query("""
                    SELECT fingerprint,result_json::text,expires_at <= clock_timestamp()
                    FROM work_command_replays WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key FOR UPDATE;
                    """))
                await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Work command claim was unavailable.");
                    if (reader.GetString(0) != command.Fingerprint) return WorkOperation<T>.Failure("idempotency_key_reused");
                    if (reader.GetBoolean(2)) return WorkOperation<T>.Failure("idempotency_key_expired");
                    resultJson = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
                // Authorization was checked before waiting; recheck after a potentially
                // long duplicate wait, so revocation during that wait is respected.
                if (!await authorizeReplay(default)) return WorkOperation<T>.Failure(command.ScopeFailureCode);
                if (!await actors.VerifyAsync(command.ActorId, cancellationToken)) return WorkOperation<T>.Failure("session_unavailable");
                if (resultJson is not null)
                {
                    var previous = JsonSerializer.Deserialize<WorkOperation<T>>(resultJson)
                        ?? throw new InvalidOperationException("Work command result was unavailable.");
                    return await authorizeReplay(previous.Value) ? previous : WorkOperation<T>.Failure(command.ScopeFailureCode);
                }
                var result = await operation();
                if (result.Succeeded)
                {
                    await using var complete = Query("""
                        UPDATE work_command_replays SET result_json=@result
                        WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;
                        """);
                    complete.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result));
                    await complete.ExecuteNonQueryAsync(cancellationToken);
                }
                return result;
            }, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Work command lacked a database acknowledgment for Organization {OrganizationId}; database code {DatabaseCode}.",
                organizationId, exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return WorkOperation<T>.Failure("work_storage_unavailable");
        }
    }
}
