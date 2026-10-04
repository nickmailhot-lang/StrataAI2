using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityHandleClaimReplayStore(PostgresConnectionFactory connections) : IIdentityHandleClaimReplayStore
{
    private void RequireScope(Guid user, Guid key)
    {
        if (!connections.OwnsIdentitySubject(user)) throw new InvalidOperationException("Handle claim retries require an owning identity subject transaction.");
        if (key == Guid.Empty) throw new ArgumentException("Handle claim retry key is required.");
    }
    public async Task<IdentityHandleClaimReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope(userId, key);
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            SELECT fingerprint,user_version,handle_version,changed,created_at,expires_at,expires_at<=clock_timestamp()
            FROM identity_handle_claim_replays WHERE user_id=@user AND key_id=@key;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("subject", userId.ToString("D")); query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("key", key);
        await using var rows = await query.ExecuteReaderAsync(cancellationToken);
        // The first result is the subject-setting capability, never a receipt.
        if (!await rows.NextResultAsync(cancellationToken) || !await rows.ReadAsync(cancellationToken)) return null;
        return new(rows.GetString(0), new(rows.GetInt64(1), rows.GetInt64(2), rows.GetBoolean(3)),
            rows.GetFieldValue<DateTimeOffset>(4), rows.GetFieldValue<DateTimeOffset>(5), rows.GetBoolean(6));
    }
    public async Task<bool> TrySaveAsync(Guid userId, Guid key, string fingerprint, HandleClaimReceipt receipt, CancellationToken cancellationToken)
    {
        RequireScope(userId, key); IdentityHandleClaimReplayShape.Require(fingerprint, receipt);
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using (var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@subject,true);", session.Connection, session.Transaction))
        {
            subject.Parameters.AddWithValue("subject", userId.ToString("D")); await subject.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var query = new NpgsqlCommand("""
            INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed)
            VALUES(@user,@key,@fingerprint,@user_version,@handle_version,@changed)
            ON CONFLICT(user_id,key_id) DO NOTHING RETURNING key_id;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("key", key);
        query.Parameters.AddWithValue("fingerprint", fingerprint); query.Parameters.AddWithValue("user_version", receipt.UserVersion);
        query.Parameters.AddWithValue("handle_version", receipt.HandleVersion); query.Parameters.AddWithValue("changed", receipt.Changed);
        return await query.ExecuteScalarAsync(cancellationToken) is Guid;
    }
}
internal static class IdentityHandleClaimReplayShape
{
    public static void Require(string fingerprint, HandleClaimReceipt receipt)
    {
        if (string.IsNullOrEmpty(fingerprint) || fingerprint.Length != 64 || fingerprint.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || receipt is null || receipt.UserVersion < 1 || receipt.HandleVersion < 1)
            throw new ArgumentException("Handle claim acknowledgment metadata is invalid.");
    }
}
