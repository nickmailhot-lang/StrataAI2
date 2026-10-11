using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.Records;

// PRD-70: real owning PostgreSQL adapter/transaction and Application commands.
// Admission/scope probes are fixture contracts, not confidentiality or HTTP proof.
internal static class RetentionHoldPersistenceContract
{
    private sealed class Admission : ICommandActorAuthorization
    { public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(true); }
    private sealed class Scope : IRetentionHoldScopeAuthorization
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> VerifyAsync(RetentionHoldScope scope, Guid actor, CancellationToken ct) => Task.FromResult(Allowed);
    }
    private sealed class AfterWrite(IRetentionHoldStore inner, Action after) : IRetentionHoldStore
    {
        public Task<RetentionHoldCandidates> ReadApplicableAsync(RetentionRecord record, CancellationToken ct) => inner.ReadApplicableAsync(record, ct);
        public Task<RetentionHold?> ReadAsync(Guid organization, Guid hold, CancellationToken ct) => inner.ReadAsync(organization, hold, ct);
        public Task<RetentionHoldReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptAsync(organization, actor, key, ct);
        public async Task<RetentionHoldWriteResult> WriteAsync(long expected, RetentionHoldWrite write, CancellationToken ct)
        { var result = await inner.WriteAsync(expected, write, ct); if (result == RetentionHoldWriteResult.Written) after(); return result; }
    }
    private static void Require(bool condition, string rule)
    { if (!condition) throw new InvalidOperationException(rule); }

    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); var actor = Guid.NewGuid(); var member = Guid.NewGuid();
        var source = Guid.NewGuid(); var hold = Guid.NewGuid(); var key = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        try
        {
            // Prospective grants are restricted to this disposable fixture. Canonical
            // provisioning retains no hold/source capabilities until activation.
            await using (var seed = new NpgsqlCommand("""
                GRANT SELECT,INSERT,UPDATE ON record_retention_holds TO strataai_api_runtime;
                GRANT SELECT,INSERT ON record_retention_hold_history,record_retention_hold_receipts TO strataai_api_runtime;
                GRANT SELECT ON record_retention_sources TO strataai_api_runtime;
                INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
                VALUES(@actor,@email,upper(@email),'Native hold owner','ACTIVE','unusable-fixture-hash',@at,@at);
                INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at,organization_type)
                VALUES(@tenant,'Native hold contract',@actor,'ACTIVE',1,@at,@at,'STRATA'),(@other,'Foreign hold contract',@actor,'ACTIVE',1,@at,@at,'STRATA');
                INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
                VALUES(@member,@tenant,@actor,'OWNER','ACTIVE',@at,@at);
                SELECT set_config('app.tenant_id',@tenant::text,false),set_config('app.identity_subject',@actor::text,false);
                INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,correlation_id,created_at)
                VALUES(@source,@tenant,@actor,'NATIVE_HOLD_SOURCE','AUDIT','native-hold-contract',@at);
                INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,category,registered_at,
                 actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
                VALUES(@tenant,'AUDIT',@source,'AUDIT_EVENT','audit.fixture',@at,@actor,@member,1,'Native hold owner','ORGANIZATION_OWNER',gen_random_uuid(),'native-hold-contract');
                SELECT set_config('app.tenant_id','',false),set_config('app.identity_subject','',false);
                """, admin))
            {
                seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", actor + "@example.test");
                seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("other", other);
                seed.Parameters.AddWithValue("member", member); seed.Parameters.AddWithValue("source", source); seed.Parameters.AddWithValue("at", at);
                await seed.ExecuteNonQueryAsync(ct);
            }
            // Prove the narrow lock privilege separately from mutation permission.
            await using (var restricted = new NpgsqlConnection(apiConnection))
            {
                await restricted.OpenAsync(ct);
                await using var transaction = await restricted.BeginTransactionAsync(ct);
                await using var context = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant::text,true);", restricted, transaction);
                context.Parameters.AddWithValue("tenant", tenant); await context.ExecuteScalarAsync(ct);
                try
                {
                    await using var locked = new NpgsqlCommand("SELECT record_id FROM record_retention_sources FOR SHARE;", restricted, transaction);
                    await locked.ExecuteScalarAsync(ct);
                    throw new InvalidOperationException("Source row lock admitted without its narrow privilege.");
                }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
                await transaction.RollbackAsync(ct);
            }
            await using (var grant = new NpgsqlCommand("GRANT UPDATE(version) ON record_retention_sources TO strataai_api_runtime;", admin))
                await grant.ExecuteNonQueryAsync(ct);
            await using (var restricted = new NpgsqlConnection(apiConnection))
            {
                await restricted.OpenAsync(ct);
                await using var transaction = await restricted.BeginTransactionAsync(ct);
                await using var context = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant::text,true);", restricted, transaction);
                context.Parameters.AddWithValue("tenant", tenant); await context.ExecuteScalarAsync(ct);
                try
                {
                    await using var mutation = new NpgsqlCommand("UPDATE record_retention_sources SET version=version;", restricted, transaction);
                    await mutation.ExecuteNonQueryAsync(ct);
                    throw new InvalidOperationException("Narrow source lock privilege permitted an actual update.");
                }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { }
                await transaction.RollbackAsync(ct);
            }
            await using var db = new PostgresConnectionFactory(apiConnection);
            var clock = new SystemClock(); var policy = new IdentityPolicy(true, false, 12, TimeSpan.FromDays(1), TimeSpan.FromHours(1));
            var identities = new PostgresIdentityStore(db); var organizations = new PostgresOrganizationStore(db, policy);
            var actors = new Admission(); var scopes = new Scope();
            var unit = new PostgresOrganizationUnitOfWork(db, actors, NullLogger<PostgresOrganizationUnitOfWork>.Instance);
            var store = new PostgresRetentionHoldStore(db, organizations, identities, actors, scopes, clock);
            var service = new RetentionHoldService(organizations, store, scopes, unit, actors, clock);
            var scope = new RetentionHoldScope(tenant, RetentionRecordClass.Audit, "AUDIT.FIXTURE", source);
            var applied = await service.ApplyAsync(tenant, actor, hold, scope, "Private native hold reason", key, "native-hold-contract", ct);
            Require(applied.Succeeded && applied.Value is { Version: 1, Status: RetentionHoldStatus.Active }, $"Native hold apply failed ({applied.ErrorCode}).");
            Require((await service.ReadAsync(tenant, actor, hold, ct)).Value == applied.Value, "Native persisted refresh differs.");
            var released = await service.ReleaseAsync(tenant, actor, hold, 1, Guid.NewGuid(), "native-hold-contract", ct);
            Require(released.Succeeded && released.Value is { Version: 2, Status: RetentionHoldStatus.Released }, "Native release failed.");
            Require((await service.ApplyAsync(tenant, actor, hold, scope, "Private native hold reason", key, "retry", ct)).Value == applied.Value,
                "Native Apply recovery substituted current Released head.");
            Require((await service.ReadAsync(tenant, actor, hold, ct)).Value == released.Value, "Native recovery reactivated a released hold.");
            Require((await service.ReadAsync(other, actor, hold, ct)).Value is null, "Native foreign Organization hold disclosed.");
            Require((await service.ApplyAsync(tenant, actor, Guid.NewGuid(), scope with { RecordId = Guid.NewGuid() }, "Private", Guid.NewGuid(), "unknown", ct)).ErrorCode == "retention_hold_source_unavailable",
                "Native unregistered source admitted by storage.");
            Require(!(await service.ReleaseAsync(tenant, actor, hold, 1, Guid.NewGuid(), "stale", ct)).Succeeded, "Native stale release admitted.");
            var record = new RetentionRecord(source, tenant, 1, RetentionRecordClass.Audit, "audit.fixture", null, at, null, null, null, true);
            var lookup = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
            {
                var candidates = await store.ReadApplicableAsync(record, ct);
                Require(candidates.ScopeComplete && candidates.Items.Single() == released.Value, "Native lookup omitted current release evidence.");
                Require(await store.ReadReceiptAsync(tenant, Guid.NewGuid(), key, ct) is null, "Native foreign actor receipt disclosed.");
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(lookup.Succeeded, "Native lookup unit failed.");
            var recoveredKey = Guid.NewGuid(); var rolledBack = Guid.NewGuid();
            var faulted = new RetentionHoldService(organizations, new AfterWrite(store, () => scopes.Allowed = false), scopes, unit, actors, clock);
            Require(!(await faulted.ApplyAsync(tenant, actor, rolledBack, scope, "Private rollback reason", recoveredKey, "rollback", ct)).Succeeded,
                "Native late scope withdrawal committed.");
            scopes.Allowed = true;
            Require((await service.ReadAsync(tenant, actor, rolledBack, ct)).Value is null, "Native failed write retained a current head.");
            Require((await service.ApplyAsync(tenant, actor, rolledBack, scope, "Private rollback reason", recoveredKey, "reclaimed", ct)).Succeeded,
                "Native rollback did not reclaim the original command key.");
            try { await store.ReadAsync(tenant, hold, ct); throw new Exception("Unowned native hold IO admitted."); }
            catch (InvalidOperationException) { }
            await using var graph = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM record_retention_holds WHERE tenant_id=@tenant),
                 (SELECT count(*) FROM record_retention_hold_history WHERE tenant_id=@tenant),
                 (SELECT count(*) FROM record_retention_hold_receipts WHERE tenant_id=@tenant),
                 (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant AND entity_type='RETENTION_HOLD');
                """, admin);
            graph.Parameters.AddWithValue("tenant", tenant); await using var rows = await graph.ExecuteReaderAsync(ct);
            Require(await rows.ReadAsync(ct) && rows.GetInt64(0)==2 && rows.GetInt64(1)==3 && rows.GetInt64(2)==3 && rows.GetInt64(3)==3,
                "Native hold committed graph or rollback counters invalid.");
            await rows.DisposeAsync();
            async Task AddCategoryHolds(int count)
            {
                await using var seed = new NpgsqlCommand("""
                    SELECT set_config('app.tenant_id',@tenant::text,false),set_config('app.identity_subject',@actor::text,false);
                    CREATE OR REPLACE FUNCTION pg_temp.seed_bounded_holds(tenant uuid,actor uuid,member uuid,n integer) RETURNS void
                    LANGUAGE plpgsql AS $$ DECLARE i integer; hold uuid; key uuid; at timestamptz; BEGIN
                    FOR i IN 1..n LOOP
                     hold:=gen_random_uuid(); key:=gen_random_uuid(); at:=clock_timestamp();
                     INSERT INTO record_retention_holds(tenant_id,id,version,record_class,category,status,reason,applied_by,applied_at)
                     VALUES(tenant,hold,1,'AUDIT','audit.fixture','ACTIVE','Private bound fixture',actor,at);
                     INSERT INTO record_retention_hold_history(tenant_id,hold_id,version,record_class,category,status,reason,applied_by,applied_at,
                      actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,key_id,correlation_id,changed_at)
                     VALUES(tenant,hold,1,'AUDIT','audit.fixture','ACTIVE','Private bound fixture',actor,at,
                      actor,member,1,'Native hold owner','ORGANIZATION_OWNER',gen_random_uuid(),key,'native-bound-contract',at);
                     INSERT INTO record_retention_hold_receipts(tenant_id,actor_id,key_id,hold_id,version,fingerprint,created_at,expires_at)
                     VALUES(tenant,actor,key,hold,1,repeat('B',64),at,at+interval '24 hours');
                    END LOOP; END $$;
                    SELECT pg_temp.seed_bounded_holds(@tenant,@actor,@member,@count);
                    SELECT set_config('app.tenant_id','',false),set_config('app.identity_subject','',false);
                    """, admin);
                seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("actor", actor);
                seed.Parameters.AddWithValue("member", member); seed.Parameters.AddWithValue("count", count);
                await seed.ExecuteNonQueryAsync(ct);
            }
            async Task CheckBound(bool complete, int count)
            {
                var read = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
                {
                    var candidates = await store.ReadApplicableAsync(record, ct);
                    Require(candidates.ScopeComplete == complete && candidates.Items.Count == count,
                        "Native hold lookup bound fabricated completeness or exceeded its limit.");
                    return OrganizationOperation<bool>.Success(true);
                }, ct);
                Require(read.Succeeded, "Native bounded lookup unit failed.");
            }
            await AddCategoryHolds(998); await CheckBound(true, 1000);
            await AddCategoryHolds(3); await CheckBound(false, 1001);
            Console.WriteLine("Native hold adapter: actual restricted owning apply/release, original recovery, current release lookup, actor-bound receipts, late scope rollback and reclaimed key passed; scope/admission are fixture probes.");
        }
        finally
        {
            await using var restore = new NpgsqlCommand("""
                REVOKE SELECT,INSERT,UPDATE ON record_retention_holds FROM strataai_api_runtime;
                REVOKE SELECT,INSERT ON record_retention_hold_history,record_retention_hold_receipts FROM strataai_api_runtime;
                REVOKE SELECT,UPDATE(version) ON record_retention_sources FROM strataai_api_runtime;
                """, admin);
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
