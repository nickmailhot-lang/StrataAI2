using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityRecoveryRequestReplayStore(PostgresConnectionFactory connections) : IIdentityRecoveryRequestReplayStore
{
    public async Task<IdentityRecoveryRequestReplay?> ReadAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            SELECT key_version,fingerprint,COALESCE(password_reset_token_id,verification_token_id),token_source,token_key_version,expires_at
            FROM identity_recovery_request_replays WHERE user_id=@user AND key_id=@key AND operation=@operation;
            """, session.Connection, session.Transaction);
        AddSubject(command, userId, key, purpose);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.NextResultAsync(cancellationToken) || !await reader.ReadAsync(cancellationToken)) return null;
        var source = reader.GetString(3) switch {
            "API_REQUEST" => RecoveryTokenSource.ApiRequest, "EMAIL_DELIVERY" => RecoveryTokenSource.EmailDelivery,
            _ => throw new InvalidOperationException("Unknown recovery token source."),
        };
        return new(reader.GetString(0), reader.GetString(1), reader.GetGuid(2), source, reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));
    }
    public async Task SaveAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, IdentityRecoveryRequestReplay replay, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            INSERT INTO identity_recovery_request_replays(user_id,key_id,operation,key_version,fingerprint,
                password_reset_token_id,verification_token_id,token_source,token_key_version,expires_at)
            VALUES(@user,@key,@operation,@version,@fingerprint,@reset,@verify,@source,@token_version,@expires);
            """, session.Connection, session.Transaction);
        AddSubject(command, userId, key, purpose);
        command.Parameters.AddWithValue("version", replay.KeyVersion);
        command.Parameters.AddWithValue("fingerprint", replay.Fingerprint);
        command.Parameters.AddWithValue("reset", NpgsqlDbType.Uuid, purpose == IdentityTokenPurpose.ResetPassword ? replay.TokenId : DBNull.Value);
        command.Parameters.AddWithValue("verify", NpgsqlDbType.Uuid, purpose == IdentityTokenPurpose.VerifyEmail ? replay.TokenId : DBNull.Value);
        command.Parameters.AddWithValue("source", replay.TokenSource switch { RecoveryTokenSource.ApiRequest => "API_REQUEST", RecoveryTokenSource.EmailDelivery => "EMAIL_DELIVERY", _ => throw new ArgumentOutOfRangeException(nameof(replay)) });
        command.Parameters.AddWithValue("token_version", replay.TokenKeyVersion);
        command.Parameters.AddWithValue("expires", replay.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static void AddSubject(NpgsqlCommand command, Guid userId, Guid key, IdentityTokenPurpose purpose)
    {
        command.Parameters.AddWithValue("subject", userId.ToString()); command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("operation", purpose switch { IdentityTokenPurpose.ResetPassword => "RESET_PASSWORD", IdentityTokenPurpose.VerifyEmail => "VERIFY_EMAIL", _ => throw new ArgumentOutOfRangeException(nameof(purpose)) });
    }
    private void RequireScope()
    {
        if (!connections.HasIdentityCommandScope) throw new InvalidOperationException("Recovery requests require an owning identity transaction.");
    }
}
