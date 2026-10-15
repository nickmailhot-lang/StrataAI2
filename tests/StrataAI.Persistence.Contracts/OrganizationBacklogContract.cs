using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

// ARCH-08-FR-006: real restricted reads; no claim, lease or payload changes.
internal static class OrganizationBacklogContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var foreign = Guid.NewGuid();
        try
        {
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO organizations(id,name,created_at,updated_at)
                  VALUES(@tenant,'Backlog fixture',now(),now()),(@foreign,'Foreign backlog fixture',now(),now());
                INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,
                  state,available_at,lease_id,worker_id,lease_expires_at)
                SELECT gen_random_uuid(),@tenant,'BACKLOG_FIXTURE',n::text,gen_random_uuid(),'fixture','private-fixture',
                  CASE WHEN n<3 THEN 'PENDING' WHEN n<5 THEN 'RUNNING' WHEN n=5 THEN 'FAILED' ELSE 'SUCCEEDED' END,
                  now()+CASE WHEN n=2 THEN interval '1 day' ELSE interval '-1 minute' END,
                  CASE WHEN n IN (3,4) THEN gen_random_uuid() END,
                  CASE WHEN n IN (3,4) THEN gen_random_uuid() END,
                  CASE WHEN n=3 THEN now()+interval '1 minute' WHEN n=4 THEN now()-interval '1 minute' END
                FROM generate_series(1,6) n;
                INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id)
                SELECT gen_random_uuid(),@foreign,'BACKLOG_FIXTURE',n::text,gen_random_uuid(),'fixture','private-foreign'
                FROM generate_series(1,7) n;
                """, admin))
            {
                seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("foreign", foreign);
                await seed.ExecuteNonQueryAsync(ct);
            }
            async Task<string> Fingerprint()
            {
                await using var query = new NpgsqlCommand("SELECT md5(string_agg(row_to_json(j)::text,'' ORDER BY id)) FROM background_jobs j WHERE tenant_id=ANY(@tenants);", admin);
                query.Parameters.AddWithValue("tenants", new[] { tenant, foreign });
                return (string)(await query.ExecuteScalarAsync(ct))!;
            }
            var before = await Fingerprint();
            await using var reader = new PostgresOrganizationJobBacklogReader(workerConnection);
            if (await reader.ReadAsync(tenant, ct) != new OrganizationJobBacklog(2, 2)
                || await reader.ReadAsync(foreign, ct) != new OrganizationJobBacklog(7, 0)
                || await reader.ReadAsync(Guid.NewGuid(), ct) != new OrganizationJobBacklog(0, 0))
                throw new InvalidOperationException("Scoped backlog counts or state semantics differ.");
            // Verify forced RLS with the same restricted login, independently of
            // the adapter's explicit tenant predicate and transaction-local GUC.
            await using var connections = new PostgresConnectionFactory(workerConnection);
            await using (var unscoped = await connections.OpenConnectionAsync(ct))
            await using (var query = new NpgsqlCommand("SELECT count(*) FROM background_jobs WHERE tenant_id=ANY(@tenants);", unscoped))
            {
                query.Parameters.AddWithValue("tenants", new[] { tenant, foreign });
                if (await query.ExecuteScalarAsync(ct) is not 0L) throw new InvalidOperationException("Unscoped backlog crossed RLS.");
            }
            await using (var scoped = await connections.OpenTenantSessionAsync(tenant, ct))
            await using (var query = new NpgsqlCommand("SELECT count(*) FROM background_jobs WHERE tenant_id=@foreign;", scoped.Connection, scoped.Transaction))
            {
                query.Parameters.AddWithValue("foreign", foreign);
                if (await query.ExecuteScalarAsync(ct) is not 0L) throw new InvalidOperationException("Scoped backlog crossed RLS.");
            }
            if (before != await Fingerprint()) throw new InvalidOperationException("Backlog observation changed persisted jobs.");
            Console.WriteLine("Organization backlog contract passed: scheduled/leased/terminal states, two isolated tenants, empty scope result, restricted RLS and unchanged jobs.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("DELETE FROM background_jobs WHERE tenant_id=ANY(@tenants); DELETE FROM organizations WHERE id=ANY(@tenants);", admin);
            cleanup.Parameters.AddWithValue("tenants", new[] { tenant, foreign });
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
