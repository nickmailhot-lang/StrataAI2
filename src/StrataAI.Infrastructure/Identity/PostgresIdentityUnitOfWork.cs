using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityUnitOfWork(PostgresConnectionFactory connections,
    ICommandActorAuthorization actors, ILogger<PostgresIdentityUnitOfWork> logger) : IIdentityUnitOfWork
{
    public async Task<IdentityOperation<RegistrationOutcome>> ExecuteRegistrationAsync(
        Func<Task<IdentityOperation<RegistrationOutcome>>> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(operation, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Registration lacked a database acknowledgment; code {DatabaseCode}.",
                exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return IdentityOperation<RegistrationOutcome>.Failure("identity_storage_unavailable");
        }
    }

    public async Task<IdentityOperation<LoginOutcome>> ExecuteSignInAsync(
        Func<Task<IdentityOperation<LoginOutcome>>> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(operation, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Sign-in lacked a database acknowledgment; code {DatabaseCode}.",
                exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return IdentityOperation<LoginOutcome>.Failure("identity_storage_unavailable");
        }
    }

    public async Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(async () =>
            {
                await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
                await using var gate = new NpgsqlCommand("SELECT id FROM users WHERE id=@actor FOR UPDATE;", session.Connection, session.Transaction);
                gate.Parameters.AddWithValue("actor", actorId);
                if (await gate.ExecuteScalarAsync(cancellationToken) is null || !await actors.VerifyAsync(actorId, cancellationToken))
                    return IdentityOperation<T>.Failure("session_unavailable");
                return await operation();
            }, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Identity command lacked a database acknowledgment for {ActorId}; code {DatabaseCode}.",
                actorId, exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return IdentityOperation<T>.Failure("identity_storage_unavailable");
        }
    }
}
