using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityTokenConsumptionReplayStore(PostgresConnectionFactory connections) : IIdentityTokenConsumptionReplayStore
{
    public async Task<IdentityTokenConsumptionReplay?> ReadAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            SELECT COALESCE(password_reset_token_id,verification_token_id),key_version,fingerprint,consumed_at,expires_at
            FROM identity_token_consumption_replays WHERE user_id=@user AND key_id=@key AND operation=@operation;
            """, session.Connection, session.Transaction);
        AddSubject(command, userId, key, purpose);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.NextResultAsync(cancellationToken) || !await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4));
    }
    public async Task SaveAsync(Guid userId, Guid key, IdentityTokenPurpose purpose, IdentityTokenConsumptionReplay replay, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            INSERT INTO identity_token_consumption_replays(user_id,key_id,operation,key_version,fingerprint,
                password_reset_token_id,verification_token_id,consumed_at,expires_at)
            VALUES(@user,@key,@operation,@version,@fingerprint,@reset,@verify,@consumed,@expires);
            """, session.Connection, session.Transaction);
        AddSubject(command, userId, key, purpose);
        command.Parameters.AddWithValue("version", replay.KeyVersion); command.Parameters.AddWithValue("fingerprint", replay.Fingerprint);
        command.Parameters.AddWithValue("reset", NpgsqlDbType.Uuid, purpose == IdentityTokenPurpose.ResetPassword ? replay.TokenId : DBNull.Value);
        command.Parameters.AddWithValue("verify", NpgsqlDbType.Uuid, purpose == IdentityTokenPurpose.VerifyEmail ? replay.TokenId : DBNull.Value);
        command.Parameters.AddWithValue("consumed", replay.ConsumedAt); command.Parameters.AddWithValue("expires", replay.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static void AddSubject(NpgsqlCommand command, Guid userId, Guid key, IdentityTokenPurpose purpose)
    {
        command.Parameters.AddWithValue("subject", userId.ToString()); command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("operation", purpose switch { IdentityTokenPurpose.ResetPassword => "RESET_PASSWORD", IdentityTokenPurpose.VerifyEmail => "VERIFY_EMAIL", _ => throw new ArgumentOutOfRangeException(nameof(purpose)) });
    }
    private void RequireScope()
    {
        if (!connections.HasIdentityCommandScope) throw new InvalidOperationException("Token retries require an owning identity transaction.");
    }
}
