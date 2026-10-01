using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityRevocationReplayStore(PostgresConnectionFactory connections) : IIdentityRevocationReplayStore
{
    public async Task<IdentityRevocationReceipt?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        await using var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@subject,true);", session.Connection, session.Transaction);
        subject.Parameters.AddWithValue("subject", userId.ToString());
        await subject.ExecuteNonQueryAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT session_id,operation,expires_at FROM identity_revocation_replays
            WHERE user_id=@user AND key_id=@key AND expires_at>clock_timestamp();
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        if (!await rows.ReadAsync(cancellationToken)) return null;
        var kind = rows.GetString(1) switch { "LOGOUT" => IdentityRevocationKind.Logout, "DEACTIVATE" => IdentityRevocationKind.Deactivate,
            _ => throw new InvalidOperationException("Invalid revocation receipt kind.") };
        return new IdentityRevocationReceipt(rows.GetGuid(0), kind, rows.GetFieldValue<DateTimeOffset>(2));
    }
    public async Task SaveAsync(Guid userId, Guid key, IdentityRevocationReceipt receipt, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            DELETE FROM identity_revocation_replays WHERE user_id=@user AND key_id=@key AND expires_at<=clock_timestamp();
            INSERT INTO identity_revocation_replays(user_id,key_id,session_id,operation,expires_at)
            VALUES (@user,@key,@session,@operation,@expires);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("subject", userId.ToString());
        command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("session", receipt.SessionId);
        command.Parameters.AddWithValue("operation", receipt.Kind == IdentityRevocationKind.Logout ? "LOGOUT" : "DEACTIVATE");
        command.Parameters.AddWithValue("expires", receipt.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private void RequireScope()
    {
        if (!connections.HasIdentityCommandScope) throw new InvalidOperationException("Revocation receipts require an owning identity transaction.");
    }
}
