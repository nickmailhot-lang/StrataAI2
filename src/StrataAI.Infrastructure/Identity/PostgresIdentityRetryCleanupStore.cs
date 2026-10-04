using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

public sealed class PostgresIdentityRetryCleanupStore(PostgresConnectionFactory connections) : IIdentityRetryCleanupStore
{
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var scope = new NpgsqlCommand("SELECT set_config('app.service_scope','GLOBAL_IDENTITY_RETRY_CLEANUP',true);", connection, transaction);
        await scope.ExecuteNonQueryAsync(cancellationToken);
        await using var purge = new NpgsqlCommand("SELECT public.purge_expired_identity_profile_replays()+public.purge_expired_identity_revocation_replays()+public.purge_expired_identity_login_replays()+public.purge_expired_identity_registration_replays()+public.purge_expired_identity_recovery_request_replays()+public.purge_expired_identity_token_consumption_replays()+public.purge_expired_identity_handle_claim_replays();", connection, transaction);
        var removed = (int)(await purge.ExecuteScalarAsync(cancellationToken))!;
        if (removed is < 0 or > 700) throw new InvalidOperationException("Invalid identity retry cleanup count.");
        await transaction.CommitAsync(cancellationToken);
        return removed;
    }
}
