using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Small owning-store fixture. It does not grant user/session admission.
internal static class ListPublicationBatchContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var foreign = Guid.NewGuid(); var actor = Guid.NewGuid();
        var board = Guid.NewGuid(); var list = Guid.NewGuid();
        async Task Sql(string text) { await using var q = new NpgsqlCommand(text, admin); await q.ExecuteNonQueryAsync(ct); }
        async Task<long> Count(string text) { await using var q = new NpgsqlCommand(text, admin); return Convert.ToInt64(await q.ExecuteScalarAsync(ct)); }
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await Sql($$"""
            INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
            VALUES ('{{actor}}','{{actor}}@example.test',upper('{{actor}}@example.test'),'Publication actor','unusable-fixture','ACTIVE',true,now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
            VALUES ('{{tenant}}','Publication fixture','{{actor}}','ACTIVE','STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            VALUES (gen_random_uuid(),'{{tenant}}','{{actor}}','OWNER','ACTIVE',now(),now());
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES ('{{board}}','{{tenant}}','Publication Board',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
            VALUES ('{{list}}','{{tenant}}','{{board}}','Publication List',repeat('5',30),now(),now());
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,archived_at,deleted_at,deleted_by,
                start_at,due_at,due_timezone,due_has_time,due_complete,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}','{{board}}','{{list}}','Publication Card','Stable body',repeat(n::text,30),
                CASE n WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,
                CASE WHEN n>1 THEN now() END,CASE WHEN n=3 THEN now() END,CASE WHEN n=3 THEN '{{actor}}'::uuid END,
                now(),now()+interval '1 day','UTC',true,false,now(),now() FROM generate_series(1,3) n;
            """);
        await using var db = new PostgresConnectionFactory(apiConnection);
        var work = new PostgresWorkManagementStore(db);
        var destination = (await work.FindListAsync(list, ct))!;
        Guid[] ids;
        await using (var q = new NpgsqlCommand($"SELECT id FROM cards WHERE tenant_id='{tenant}' ORDER BY id;", admin))
        await using (var r = await q.ExecuteReaderAsync(ct))
        { var rows = new List<Guid>(); while (await r.ReadAsync(ct)) rows.Add(r.GetGuid(0)); ids = rows.ToArray(); }
        Require(ids.Length == 3, "Publication fixture lost a lifecycle.");
        try { await work.ReadCardsAsync(destination, ids, ct); throw new InvalidOperationException("Unowned batch read was accepted."); }
        catch (InvalidOperationException e) when (e.Message == "List publication requires its owning command transaction.") { }
        await db.ExecuteTenantCommandAsync(tenant, async () =>
        {
            var rows = await work.ReadCardsAsync(destination, ids, ct);
            Require(rows.Count == 3 && rows.Values.Select(c => c.LifecycleState).Distinct().Count() == 3, "Batch read omitted a lifecycle.");
            foreach (var id in ids) Require(rows[id] == await work.FindCardAsync(id, ct, includeDeleted: true), "Batch mapping changed Card content/dates/history.");
            await work.AppendCardAuditsAsync(destination, actor, ids, "list-batch-test", ct);
            return false;
        }, x => x, ct);
        Require(await Count($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}';") == 0, "Owning rollback retained batch audits.");
        foreach (var invalid in new[] { new[] { ids[0], ids[0] }, new[] { ids[0], Guid.NewGuid() }, new[] { Guid.Empty } })
        {
            try
            {
                await db.ExecuteTenantCommandAsync(tenant, async () => { await work.AppendCardAuditsAsync(destination, actor, invalid, "list-batch-test", ct); return true; }, x => x, ct);
                throw new InvalidOperationException("Invalid batch audit was accepted.");
            }
            catch (ArgumentException e) when (e.Message.StartsWith("List publication requires distinct nonempty Card references.", StringComparison.Ordinal)) { }
            catch (InvalidOperationException e) when (e.Message == "List publication audit omitted a Card reference.") { }
            Require(await Count($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}';") == 0, "Invalid batch retained partial audits.");
        }
        await db.ExecuteTenantCommandAsync(foreign, async () =>
        {
            Require((await work.ReadCardsAsync(destination with { OrganizationId = foreign }, ids, ct)).Count == 0, "Batch read disclosed another tenant.");
            return true;
        }, x => x, ct);
        var events = new PostgresWorkEventStore(db, new PostgresBackgroundJobStore(db));
        var changes = ids.Select(id => new WorkEvent(Guid.NewGuid(), tenant, board, actor, "CARD_MOVED", "Card", id, 1, "list-batch-test", DateTimeOffset.UtcNow)).ToArray();
        await Sql("REVOKE INSERT ON background_jobs FROM strataai_api_runtime;");
        try
        {
            try
            {
                await db.ExecuteTenantCommandAsync(tenant, async () =>
                { await work.AppendCardAuditsAsync(destination, actor, ids, "list-batch-test", ct); await events.AppendBatchAsync(changes, ct); return true; }, x => x, ct);
                throw new InvalidOperationException("Revoked job publication was accepted.");
            }
            catch (PostgresException e) when (e.SqlState == "42501") { }
        }
        finally { await Sql("GRANT INSERT ON background_jobs TO strataai_api_runtime;"); }
        Require(await Count($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}';") == 0, "Late job failure retained batch audits.");
        Require(await Count($"SELECT count(*) FROM work_events WHERE tenant_id='{tenant}';") == 0, "Late job failure retained batch events.");
        await db.ExecuteTenantCommandAsync(tenant, async () =>
        { await work.AppendCardAuditsAsync(destination, actor, ids, "list-batch-test", ct); await events.AppendBatchAsync(changes, ct); return true; }, x => x, ct);
        Require(await Count($"SELECT count(*) FROM audit_events WHERE tenant_id='{tenant}' AND actor_id='{actor}' AND event_type='CARD_MOVED' AND entity_type='Card' AND safe_metadata='{{}}'::jsonb;") == 3, "Complete batch audit commit failed.");
        Require(await Count($"SELECT count(*) FROM background_jobs WHERE tenant_id='{tenant}';") == 3, "Complete batch outbox commit failed.");
        Console.WriteLine("List publication batches: three lifecycles, complete mapping, tenant isolation, invalid references, owning/late-outbox rollback and complete commit passed.");
    }
}
