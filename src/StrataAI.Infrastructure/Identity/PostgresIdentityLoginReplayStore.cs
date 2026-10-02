using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;
internal sealed class PostgresIdentityLoginReplayStore(PostgresConnectionFactory connections) : IIdentityLoginReplayStore
{
    public async Task<IdentityLoginReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope(); await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            SELECT session_id,key_version,fingerprint,expires_at FROM identity_login_replays WHERE user_id=@user AND key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("subject", userId.ToString()); command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        // set_config is its own result set; never interpret it as a receipt.
        if (!await reader.NextResultAsync(cancellationToken) || !await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3));
    }
    public async Task SaveAsync(Guid userId, Guid key, IdentityLoginReplay replay, CancellationToken cancellationToken)
    {
        RequireScope(); await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            INSERT INTO identity_login_replays(user_id,key_id,session_id,key_version,fingerprint,expires_at)
            VALUES(@user,@key,@session,@version,@fingerprint,@expires);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("subject", userId.ToString()); command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("session", replay.SessionId); command.Parameters.AddWithValue("version", replay.KeyVersion);
        command.Parameters.AddWithValue("fingerprint", replay.Fingerprint); command.Parameters.AddWithValue("expires", replay.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private void RequireScope() { if (!connections.HasIdentityCommandScope) throw new InvalidOperationException("Sign-in retries require an owning identity transaction."); }
}
