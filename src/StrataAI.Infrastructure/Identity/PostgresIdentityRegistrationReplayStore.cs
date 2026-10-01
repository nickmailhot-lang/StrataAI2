using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityRegistrationReplayStore(PostgresConnectionFactory connections) : IIdentityRegistrationReplayStore
{
    public async Task<IdentityRegistrationReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            SELECT key_version,fingerprint,verification_token_id,verification_source,verification_key_version,expires_at
            FROM identity_registration_replays WHERE user_id=@user AND key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("subject", userId.ToString());
        command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.NextResultAsync(cancellationToken) || !await reader.ReadAsync(cancellationToken)) return null;
        var source = reader.GetString(3) switch {
            "NONE" => RegistrationVerificationSource.None,
            "API_REGISTRATION" => RegistrationVerificationSource.ApiRegistration,
            "EMAIL_DELIVERY" => RegistrationVerificationSource.EmailDelivery,
            _ => throw new InvalidOperationException("Unknown registration verification source."),
        };
        return new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
            source, reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task SaveAsync(Guid userId, Guid key, IdentityRegistrationReplay replay, CancellationToken cancellationToken)
    {
        RequireScope();
        var source = replay.VerificationSource switch {
            RegistrationVerificationSource.None => "NONE",
            RegistrationVerificationSource.ApiRegistration => "API_REGISTRATION",
            RegistrationVerificationSource.EmailDelivery => "EMAIL_DELIVERY",
            _ => throw new ArgumentOutOfRangeException(nameof(replay)),
        };
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            INSERT INTO identity_registration_replays(user_id,key_id,key_version,fingerprint,
                verification_token_id,verification_source,verification_key_version,expires_at)
            VALUES(@user,@key,@version,@fingerprint,@token,@source,@token_version,@expires);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("subject", userId.ToString());
        command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("version", replay.KeyVersion); command.Parameters.AddWithValue("fingerprint", replay.Fingerprint);
        command.Parameters.AddWithValue("token", NpgsqlDbType.Uuid, (object?)replay.VerificationTokenId ?? DBNull.Value);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("token_version", NpgsqlDbType.Text, (object?)replay.VerificationKeyVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("expires", replay.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void RequireScope()
    {
        if (!connections.HasIdentityCommandScope) throw new InvalidOperationException("Registration retries require an owning identity transaction.");
    }
}
