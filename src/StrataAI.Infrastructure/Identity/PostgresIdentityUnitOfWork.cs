using Microsoft.Extensions.Logging;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityUnitOfWork(PostgresConnectionFactory connections,
    ICommandActorAuthorization actors, ILogger<PostgresIdentityUnitOfWork> logger, IdentityRevocationReplayExecutor revocations,
    IAccountDeactivationOwnership ownership) : IIdentityUnitOfWork
{
    public async Task<IdentityOperation<bool>> ExecuteDeactivationAsync(Guid actorId,
        Func<Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default, string correlationId = "")
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(async () =>
            {
                var plan = await ownership.PrepareAsync(actorId, cancellationToken);
                await using var root = await connections.OpenGlobalSessionAsync(cancellationToken);
                await using var account = new NpgsqlCommand("SELECT id FROM users WHERE id=@actor FOR UPDATE;", root.Connection, root.Transaction);
                account.Parameters.AddWithValue("actor", actorId);
                if (await account.ExecuteScalarAsync(cancellationToken) is null || !await actors.VerifyAsync(actorId, cancellationToken))
                    return IdentityOperation<bool>.Failure("session_unavailable");
                var error = await ownership.CheckAsync(plan, cancellationToken);
                if (error is not null) return IdentityOperation<bool>.Failure(error);
                if (!await actors.VerifyAsync(actorId, cancellationToken)) return IdentityOperation<bool>.Failure("session_unavailable");
                var result = await operation();
                if (result.Succeeded) await ownership.CleanupAssignmentsAsync(plan, correlationId, cancellationToken);
                return result;
            }, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Account deactivation lacked a database acknowledgment; code {DatabaseCode}.",
                exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return IdentityOperation<bool>.Failure("identity_storage_unavailable");
        }
    }
    public async Task<IdentityOperation<bool>> ExecuteRevocationAsync(Guid expectedActor, string sessionHash, Guid key,
        IdentityRevocationKind kind, string correlationId, Func<Guid, Task<IdentityOperation<bool>>> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(async () =>
            {
                if (kind != IdentityRevocationKind.Deactivate || key == Guid.Empty)
                    return await revocations.ExecuteAsync(expectedActor, sessionHash, key, kind, operation, cancellationToken);
                await using var root = await connections.OpenGlobalSessionAsync(cancellationToken);
                await using var route = new NpgsqlCommand("SELECT user_id FROM sessions WHERE token_hash=@hash;", root.Connection, root.Transaction);
                route.Parameters.AddWithValue("hash", sessionHash);
                if (await route.ExecuteScalarAsync(cancellationToken) is not Guid subject || (expectedActor != Guid.Empty && expectedActor != subject))
                    return IdentityOperation<bool>.Failure("session_unavailable");
                var plan = await ownership.PrepareAsync(subject, cancellationToken);
                // Completed receipts acknowledge without rerunning the floor check.
                return await revocations.ExecuteAsync(expectedActor, sessionHash, key, kind, async actor =>
                {
                    var error = await ownership.CheckAsync(plan, cancellationToken);
                    if (error is not null) return IdentityOperation<bool>.Failure(error);
                    if (!await actors.VerifyAsync(actor, cancellationToken)) return IdentityOperation<bool>.Failure("session_unavailable");
                    var result = await operation(actor);
                    if (result.Succeeded) await ownership.CleanupAssignmentsAsync(plan, correlationId, cancellationToken);
                    return result;
                }, cancellationToken);
            }, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Revocation command lacked a database acknowledgment. CorrelationId {CorrelationId}; code {DatabaseCode}.",
                correlationId, exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return IdentityOperation<bool>.Failure("identity_storage_unavailable");
        }
    }
    public async Task<T> ExecuteRecoveryRequestAsync<T>(Func<Task<T>> operation, T neutralResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(operation, _ => true, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning("Recovery request rolled back; code {DatabaseCode}. No delivery acknowledgment is claimed.",
                exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return neutralResult;
        }
    }

    public async Task<IdentityOperation<UserProfile>> ExecuteTokenProofAsync(
        Func<Task<IdentityOperation<UserProfile>>> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(operation, result => result.Succeeded, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning("Identity token command lacked a database acknowledgment; code {DatabaseCode}.",
                exception is PostgresException postgres ? postgres.SqlState : "connection_error");
            return IdentityOperation<UserProfile>.Failure("identity_storage_unavailable");
        }
    }

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

    public Task<IdentityOperation<T>> ExecuteObservationAsync<T>(Guid actorId, Guid? organizationId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default)
        => ExecuteActorAsync(actorId, operation, cancellationToken, organizationId, observation: true);

    public Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default)
        => ExecuteActorAsync(actorId, operation, cancellationToken, null, observation: false);

    private async Task<IdentityOperation<T>> ExecuteActorAsync<T>(Guid actorId,
        Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken, Guid? organizationId, bool observation)
    {
        try
        {
            return await connections.ExecuteIdentityCommandAsync(async () =>
            {
                if (observation && organizationId is Guid organization)
                    await connections.ExecuteIdentityOrganizationCleanupAsync(organization, async () => {
                        await using var tenant = await connections.OpenTenantSessionAsync(organization, cancellationToken);
                        await using var parent = new NpgsqlCommand("SELECT id FROM organizations WHERE id=@tenant AND status='ACTIVE' FOR SHARE;", tenant.Connection, tenant.Transaction);
                        parent.Parameters.AddWithValue("tenant", organization);
                        await parent.ExecuteScalarAsync(cancellationToken);
                    }, cancellationToken);
                await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
                if (observation) {
                    await using var history = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('strataai:interaction:' || @actor::text,0));", session.Connection, session.Transaction);
                    history.Parameters.AddWithValue("actor", actorId); await history.ExecuteNonQueryAsync(cancellationToken);
                }
                await using var gate = new NpgsqlCommand(observation
                    ? "SELECT id FROM users WHERE id=@actor FOR SHARE;"
                    : "SELECT id FROM users WHERE id=@actor FOR UPDATE;", session.Connection, session.Transaction);
                gate.Parameters.AddWithValue("actor", actorId);
                if (await gate.ExecuteScalarAsync(cancellationToken) is null || !await actors.VerifyAsync(actorId, cancellationToken))
                    return IdentityOperation<T>.Failure("session_unavailable");
                using var subject = connections.EnterIdentitySubject(actorId);
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
