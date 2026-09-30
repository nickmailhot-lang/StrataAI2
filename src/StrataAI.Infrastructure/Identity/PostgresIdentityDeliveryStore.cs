using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

public sealed class PostgresIdentityDeliveryStore(string connectionString) : IIdentityDeliveryStore,IAsyncDisposable
{
    private readonly PostgresConnectionFactory connections=new(connectionString);
    public ValueTask DisposeAsync()=>connections.DisposeAsync();
    public async Task<IdentityDeliveryJob?> ClaimAsync(Guid workerId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection,transaction,cancellationToken);
        IdentityDeliveryJob? job;
        await using (var command = new NpgsqlCommand("""
            SELECT id,user_id,purpose,key_id,correlation_id,recipient_email,sender_address,public_origin,provider_account,
                attempt_count,lease_id,worker_id,lease_expires_at,expires_at FROM claim_identity_delivery(@worker);
            """,connection,transaction))
        {
            command.Parameters.AddWithValue("worker",workerId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            job = await reader.ReadAsync(cancellationToken) ? new IdentityDeliveryJob(
                reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2)=="VERIFY_EMAIL" ? IdentityTokenPurpose.VerifyEmail : IdentityTokenPurpose.ResetPassword,
                reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),
                reader.GetInt32(9),reader.GetGuid(10),reader.GetGuid(11),reader.GetFieldValue<DateTimeOffset>(12),reader.GetFieldValue<DateTimeOffset>(13)) : null;
        }
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task<string?> GetUsableTokenHashAsync(IdentityDeliveryJob job, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var verification = job.Purpose==IdentityTokenPurpose.VerifyEmail;
        var table = verification ? "email_verification_tokens" : "password_reset_tokens";
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT t.token_hash FROM {table} t JOIN users u ON u.id=t.user_id
            WHERE t.id=@id AND t.user_id=@user AND t.used_at IS NULL AND t.revoked_at IS NULL AND t.expires_at>@now
                AND u.email=@email AND u.status {(verification ? "= 'PENDING_VERIFICATION' AND NOT u.email_verified" : "IN ('ACTIVE','PENDING_VERIFICATION')")};
            """,connection);
        command.Parameters.AddWithValue("id",job.Id);
        command.Parameters.AddWithValue("user",job.UserId);
        command.Parameters.AddWithValue("now",now);
        command.Parameters.AddWithValue("email",job.RecipientEmail);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<bool> FinishAsync(IdentityDeliveryJob job, IdentityDeliveryOutcome outcome, string? errorCode, Guid? receiptId, CancellationToken cancellationToken)
    {
        var state = outcome switch
        {
            IdentityDeliveryOutcome.Sent=>"SENT",IdentityDeliveryOutcome.Cancelled=>"CANCELLED",
            IdentityDeliveryOutcome.Retry=>"RETRY",IdentityDeliveryOutcome.Failed=>"FAILED",
            _=>throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection,transaction,cancellationToken);
        await using var command = new NpgsqlCommand("SELECT finish_identity_delivery(@id,@lease,@worker,@outcome,@error,@receipt);",connection,transaction);
        command.Parameters.AddWithValue("id",job.Id);
        command.Parameters.AddWithValue("lease",job.LeaseId);
        command.Parameters.AddWithValue("worker",job.WorkerId);
        command.Parameters.AddWithValue("outcome",state);
        command.Parameters.AddWithValue("error",NpgsqlDbType.Text,(object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("receipt",NpgsqlDbType.Uuid,(object?)receiptId ?? DBNull.Value);
        var changed = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private static async Task SetScopeAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.service_scope','GLOBAL_IDENTITY_MAIL',true);",connection,transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
