using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Persistence;

internal static class IdentityBacklogContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string identityConnection, string apiConnection,
        string workerConnection, CancellationToken ct)
    {
        var owners = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using var reader = new PostgresIdentityDeliveryBacklogReader(identityConnection);
        var baseline = await reader.ReadAsync(ct);
        try
        {
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
                SELECT id,id::text||'@example.test',upper(id::text||'@example.test'),'Identity backlog fixture','ACTIVE','unused-fixture-hash',now(),now()
                FROM unnest(@owners) id;
                INSERT INTO password_reset_tokens(id,user_id,token_hash,created_at,expires_at)
                SELECT gen_random_uuid(),(@owners)[1+(n%2)],md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text),now(),now()+interval '10 minutes'
                FROM generate_series(1,7) n;
                INSERT INTO identity_delivery_jobs(id,user_id,purpose,password_reset_token_id,key_id,correlation_id,
                  recipient_email,sender_address,public_origin,provider_account,state,available_at,lease_id,worker_id,
                  lease_expires_at,provider_message_id,created_at,expires_at)
                SELECT id,user_id,'RESET_PASSWORD',id,'fixture','private-backlog-fixture',user_id::text||'@example.test',
                  'sender@example.test','https://fixture.example.test','fixture',
                  CASE WHEN n<3 THEN 'PENDING' WHEN n<5 THEN 'RUNNING' WHEN n=5 THEN 'SENT' WHEN n=6 THEN 'FAILED' ELSE 'CANCELLED' END,
                  now()+CASE WHEN n=2 THEN interval '1 hour' ELSE interval '-1 minute' END,
                  CASE WHEN n IN (3,4) THEN gen_random_uuid() END,CASE WHEN n IN (3,4) THEN gen_random_uuid() END,
                  CASE WHEN n=3 THEN now()+interval '1 minute' WHEN n=4 THEN now()-interval '1 minute' END,
                  CASE WHEN n=5 THEN gen_random_uuid() END,now(),now()+interval '10 minutes'
                FROM (SELECT t.*,row_number() OVER (ORDER BY id) n FROM password_reset_tokens t WHERE user_id=ANY(@owners)) tokens;
                """, admin))
            {
                seed.Parameters.AddWithValue("owners", owners); await seed.ExecuteNonQueryAsync(ct);
            }
            async Task<string> Fingerprint()
            {
                await using var query = new NpgsqlCommand("SELECT md5(string_agg(row_to_json(j)::text,'' ORDER BY id)) FROM identity_delivery_jobs j WHERE user_id=ANY(@owners) HAVING count(*)=7;", admin);
                query.Parameters.AddWithValue("owners", owners);
                return await query.ExecuteScalarAsync(ct) as string ?? throw new InvalidOperationException("Identity fixture rows missing.");
            }
            var before = await Fingerprint();
            var expected = new IdentityDeliveryBacklog(baseline.Pending + 2, baseline.Running + 2);
            if (await reader.ReadAsync(ct) != expected) throw new InvalidOperationException("Identity backlog state counts differ.");
            await using var mail = new PostgresConnectionFactory(identityConnection);
            await using (var unscoped = await mail.OpenConnectionAsync(ct))
            {
                await using var count = new NpgsqlCommand("SELECT count(*) FROM identity_delivery_jobs WHERE user_id=ANY(@owners);", unscoped);
                count.Parameters.AddWithValue("owners", owners);
                if (await count.ExecuteScalarAsync(ct) is not 0L) throw new InvalidOperationException("Identity backlog escaped service RLS.");
            }
            async Task Denied(string connectionString, string sql)
            {
                await using var connections = new PostgresConnectionFactory(connectionString);
                await using var connection = await connections.OpenConnectionAsync(ct);
                try
                {
                    await using var query = new NpgsqlCommand(sql, connection);
                    await query.ExecuteScalarAsync(ct);
                }
                catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege) { return; }
                throw new InvalidOperationException("Backlog diagnostic broadened database privileges.");
            }
            await Denied(identityConnection, "SELECT count(*) FROM organizations;");
            await Denied(identityConnection, "SELECT password_hash FROM users LIMIT 1;");
            await Denied(apiConnection, "SELECT count(*) FROM identity_delivery_jobs;");
            await Denied(workerConnection, "SELECT count(*) FROM identity_delivery_jobs;");
            if (before != await Fingerprint()) throw new InvalidOperationException("Identity observation changed persisted jobs.");
            Console.WriteLine("Identity backlog contract passed: two subjects, scheduled/leased/terminal states, service RLS, restricted columns/roles and unchanged jobs.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM identity_delivery_jobs WHERE user_id=ANY(@owners);
                DELETE FROM password_reset_tokens WHERE user_id=ANY(@owners);
                DELETE FROM users WHERE id=ANY(@owners);
                """, admin);
            cleanup.Parameters.AddWithValue("owners", owners); await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
        if (await reader.ReadAsync(ct) != baseline) throw new InvalidOperationException("Identity backlog did not return to baseline.");
    }
}
