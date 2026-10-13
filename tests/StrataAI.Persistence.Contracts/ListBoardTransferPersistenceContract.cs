using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Real restricted persistence; actor/session authorization and publication are
// Application/HTTP contracts, not supplied by this owning-store fixture.
internal static class ListBoardTransferPersistenceContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var foreign = Guid.NewGuid(); var actor = Guid.NewGuid();
        var member = Guid.NewGuid(); var departing = Guid.NewGuid(); var unverified = Guid.NewGuid();
        var source = Guid.NewGuid(); var destination = Guid.NewGuid(); var foreignBoard = Guid.NewGuid();
        var list = Guid.NewGuid(); var anchor = Guid.NewGuid(); var configured = Guid.NewGuid(); var shared = Guid.NewGuid(); var deleted = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        async Task Sql(string text) { await using var q = new NpgsqlCommand(text, admin) { CommandTimeout = 120 }; await q.ExecuteNonQueryAsync(ct); }
        async Task<string> Scalar(string text) { await using var q = new NpgsqlCommand(text, admin) { CommandTimeout = 120 }; return (await q.ExecuteScalarAsync(ct))?.ToString() ?? ""; }
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await Sql($$"""
            BEGIN;
            INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
            SELECT u,u::text||'@example.test',upper(u::text||'@example.test'),'Transfer fixture','unusable-fixture','ACTIVE',u<>'{{unverified}}'::uuid,now(),now()
            FROM unnest(ARRAY['{{actor}}'::uuid,'{{member}}'::uuid,'{{departing}}'::uuid,'{{unverified}}'::uuid]) u;
            INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at) VALUES
              ('{{tenant}}','Transfer fixture','{{actor}}','ACTIVE','STRATA',now(),now()),
              ('{{foreign}}','Other scope','{{actor}}','ACTIVE','STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}',u,CASE WHEN u='{{actor}}'::uuid THEN 'OWNER' ELSE 'MEMBER' END,'ACTIVE',now(),now()
              FROM unnest(ARRAY['{{actor}}'::uuid,'{{member}}'::uuid,'{{departing}}'::uuid,'{{unverified}}'::uuid]) u;
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
              ('{{source}}','{{tenant}}','Source',now(),now()),('{{destination}}','{{tenant}}','Destination',now(),now()),('{{foreignBoard}}','{{foreign}}','Other',now(),now());
            INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}',b,u,'MEMBER','ACTIVE',now(),now()
              FROM unnest(ARRAY['{{source}}'::uuid,'{{destination}}'::uuid]) b
              CROSS JOIN unnest(ARRAY['{{actor}}'::uuid,'{{member}}'::uuid,'{{departing}}'::uuid,'{{unverified}}'::uuid]) u
              WHERE b='{{source}}'::uuid OR u<>'{{departing}}'::uuid;
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
              ('{{list}}','{{tenant}}','{{source}}','Stable List',repeat('5',30),now(),now()),
              ('{{configured}}','{{tenant}}','{{source}}','Configured intake',repeat('6',30),now(),now()),
              ('{{anchor}}','{{tenant}}','{{destination}}','Destination anchor',repeat('5',30),now(),now());
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,archived_at,deleted_at,deleted_by,created_at,updated_at)
            SELECT gen_random_uuid(),'{{tenant}}','{{source}}','{{list}}','Stable Card '||n,'Stable body',repeat(n::text,30),
              CASE n WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,
              CASE WHEN n>=2 THEN now() END,CASE WHEN n=3 THEN now() END,CASE WHEN n=3 THEN '{{actor}}'::uuid END,now(),now()
              FROM generate_series(1,3) n;
            INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank,status,deleted_at,created_at,updated_at) VALUES
              ('{{shared}}','{{tenant}}','{{source}}','Shared definition','blue',repeat('5',30),'ACTIVE',NULL,now(),now()),
              ('{{deleted}}','{{tenant}}','{{source}}','Deleted definition','red',repeat('6',30),'DELETED',now(),now(),now());
            INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at,version)
              SELECT tenant_id,board_id,id,l,now(),now(),4 FROM cards CROSS JOIN unnest(ARRAY['{{shared}}'::uuid,'{{deleted}}'::uuid]) l WHERE tenant_id='{{tenant}}';
            INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by,created_at,updated_at,version)
              SELECT tenant_id,board_id,id,u,'{{actor}}',now(),now(),7 FROM cards CROSS JOIN unnest(ARRAY['{{member}}'::uuid,'{{departing}}'::uuid,'{{unverified}}'::uuid]) u WHERE tenant_id='{{tenant}}';
            INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at)
              SELECT gen_random_uuid(),tenant_id,id,'Stable checklist',repeat('5',30),now(),now() FROM cards WHERE tenant_id='{{tenant}}';
            INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,completed,completed_at,completed_by,created_at,updated_at)
              SELECT gen_random_uuid(),tenant_id,id,'Completed item',repeat('5',30),true,now(),'{{actor}}',now(),now() FROM checklists WHERE tenant_id='{{tenant}}';
            INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
              SELECT gen_random_uuid(),tenant_id,id,'{{actor}}','Stable comment',now(),now() FROM cards WHERE tenant_id='{{tenant}}';
            INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
              SELECT gen_random_uuid(),tenant_id,id,'{{actor}}','URL','Stable link','https://example.test/reference','NOT_APPLICABLE',now(),now() FROM cards WHERE tenant_id='{{tenant}}';
            COMMIT;
            """);
        await Sql($$"""
            BEGIN;
            SELECT set_config('app.tenant_id','{{tenant}}',true);
            DO $$ DECLARE at timestamptz:=clock_timestamp(); payload jsonb; key uuid:=gen_random_uuid();
            BEGIN
              payload:=jsonb_build_object('OrganizationId','{{tenant}}'::uuid,'Version',1,
                'Configuration',jsonb_build_object('LegalName','Transfer fixture','Jurisdiction','CA-BC','Timezone','UTC',
                  'IntakeBoardId','{{source}}'::uuid,'IntakeListId','{{configured}}'::uuid),
                'OrganizationName','Transfer fixture','OrganizationType','STRATA','OrganizationVersion',1,
                'ActorId','{{actor}}'::uuid,'EventId',gen_random_uuid(),'CorrelationId','transfer-intake-fixture','CreatedAt',at,'UpdatedAt',at);
              INSERT INTO organization_configurations(tenant_id,version,record_json) VALUES('{{tenant}}',1,payload);
              INSERT INTO organization_configuration_history(tenant_id,version,record_json) VALUES('{{tenant}}',1,payload);
              INSERT INTO organization_configuration_receipts(tenant_id,actor_id,key_id,version,receipt_json)
                VALUES('{{tenant}}','{{actor}}',key,1,jsonb_build_object('ActorId','{{actor}}'::uuid,'Key',key,
                  'Fingerprint',repeat('A',64),'Result',payload,'ExpiresAt',at+interval '24 hours'));
            END $$;
            COMMIT;
            """);
        string GraphHash(bool stable) => $$"""
            SELECT md5(jsonb_build_object(
              'lists',(SELECT jsonb_agg({{(stable ? "to_jsonb(l)-'board_id'-'rank'-'version'-'updated_at'" : "to_jsonb(l)")}} ORDER BY id) FROM board_lists l WHERE tenant_id='{{tenant}}'),
              'cards',(SELECT jsonb_agg({{(stable ? "to_jsonb(c)-'board_id'-'version'-'updated_at'" : "to_jsonb(c)")}} ORDER BY id) FROM cards c WHERE tenant_id='{{tenant}}'),
              'labels',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_labels l WHERE tenant_id='{{tenant}}'),
              'associations',(SELECT jsonb_agg(to_jsonb(a) ORDER BY card_id,label_id) FROM card_labels a WHERE tenant_id='{{tenant}}'),
              'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY card_id,user_id) FROM card_members m WHERE tenant_id='{{tenant}}'),
              'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE tenant_id='{{tenant}}'),
              'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM checklist_items i WHERE tenant_id='{{tenant}}'),
              'comments',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM card_comments c WHERE tenant_id='{{tenant}}'),
              'attachments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE tenant_id='{{tenant}}'),
              'configuration',(SELECT jsonb_agg(to_jsonb(c)) FROM organization_configurations c WHERE tenant_id='{{tenant}}'),
              'history',(SELECT jsonb_agg(to_jsonb(h) ORDER BY version) FROM organization_configuration_history h WHERE tenant_id='{{tenant}}'),
              'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY version) FROM organization_configuration_receipts r WHERE tenant_id='{{tenant}}'),
              'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='{{tenant}}'),
              'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY version) FROM organization_configuration_events e WHERE tenant_id='{{tenant}}'))::text);
            """;
        string StableChildren() => $$"""
            SELECT md5(jsonb_build_object(
              'list',(SELECT to_jsonb(l)-'board_id'-'rank'-'version'-'updated_at' FROM board_lists l WHERE id='{{list}}'),
              'cards',(SELECT jsonb_agg(to_jsonb(c)-'board_id'-'version'-'updated_at' ORDER BY id) FROM cards c WHERE tenant_id='{{tenant}}' AND list_id='{{list}}'),
              'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE tenant_id='{{tenant}}'),
              'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM checklist_items i WHERE tenant_id='{{tenant}}'),
              'comments',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM card_comments c WHERE tenant_id='{{tenant}}'),
              'attachments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE tenant_id='{{tenant}}'))::text);
            """;
        await using var db = new PostgresConnectionFactory(apiConnection); var store = new PostgresWorkManagementStore(db);
        async Task<ListBoardTransfer?> Move(Guid from, Guid to, long version, Guid? before = null) =>
            await db.ExecuteTenantCommandAsync(tenant, async () =>
            {
                await using var session = await db.OpenTenantSessionAsync(tenant, ct);
                await using var setting = new NpgsqlCommand("SELECT current_setting('enable_seqscan')||':'||current_setting('app.tenant_id')||':'||txid_current()::text||':'||current_user;", session.Connection, session.Transaction);
                var scopeBefore = (string)(await setting.ExecuteScalarAsync(ct))!;
                var moved = await store.TransferListBoardAsync(tenant, from, list, to, version, now, true, before, ct);
                Require((string)(await setting.ExecuteScalarAsync(ct))! == scopeBefore,
                    "Transfer changed its caller's planner setting, tenant, transaction or runtime identity.");
                return moved;
            }, value => value is not null, ct);
        var beforeRefusal = await Scalar(GraphHash(false));
        try { await store.TransferListBoardAsync(tenant, source, list, destination, 1, now, true, null, ct); throw new InvalidOperationException("Unowned transfer was admitted."); }
        catch (InvalidOperationException e) when (e.Message == "List transfer requires its owning command transaction.") { }
        foreach (var refused in new[] { await Move(source, foreignBoard, 1), await Move(source, destination, 99), await Move(source, destination, 1, Guid.NewGuid()) })
            Require(refused is null, "Foreign scope, stale version or unknown anchor was admitted.");
        var bound = await db.ExecuteTenantCommandAsync(tenant,
            () => store.TransferListBoardAsync(tenant, source, configured, destination, 1, now, true, null, ct), value => value is not null, ct);
        Require(bound is null, "Transfer rewrote or bypassed the owning configuration's current intake binding.");
        var savedClock = now; now = DateTimeOffset.UnixEpoch;
        Require(await Move(source, destination, 1) is null, "Backwards transfer clock was admitted."); now = savedClock;
        Require(await Scalar(GraphHash(false)) == beforeRefusal, "Refused transfer changed its complete graph.");
        var extra = Guid.NewGuid(); var exhausted = Guid.NewGuid();
        await Sql($$"""
            INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank,created_at,updated_at) VALUES
              ('{{extra}}','{{tenant}}','{{source}}','Second planned definition','green',repeat('6',30),now(),now()),
              ('{{exhausted}}','{{tenant}}','{{destination}}','Exhausted destination','blue',repeat('9',29)||'7',now(),now());
            INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at)
              SELECT tenant_id,board_id,id,'{{extra}}',now(),now() FROM cards WHERE tenant_id='{{tenant}}' AND lifecycle_state='ACTIVE';
            """);
        var beforeExhaustion = await Scalar(GraphHash(false));
        try { await Move(source, destination, 1); throw new InvalidOperationException("Exhausted definition positions were admitted."); }
        catch (RankSpaceExhaustedException) { }
        Require(await Scalar(GraphHash(false)) == beforeExhaustion, "Later definition rank exhaustion left partial copies.");
        await Sql($$"""
            DELETE FROM card_labels WHERE tenant_id='{{tenant}}' AND label_id='{{extra}}';
            DELETE FROM board_labels WHERE tenant_id='{{tenant}}' AND id IN ('{{extra}}','{{exhausted}}');
            """);
        Require(await Scalar(GraphHash(false)) == beforeRefusal, "Exhaustion fixture did not restore its original source.");
        // A late real restricted audit failure must roll back copied definitions,
        // both parent updates and all association changes in the owning transaction.
        await Sql("REVOKE INSERT ON audit_events FROM strataai_api_runtime;");
        try
        {
            try
            {
                await db.ExecuteTenantCommandAsync(tenant, async () =>
                {
                    var moved = await store.TransferListBoardAsync(tenant, source, list, destination, 1, now, true, null, ct);
                    Require(moved is not null, "Late rollback fixture did not perform a transfer.");
                    await store.AppendAuditAsync(tenant, actor, "LIST_MOVED", "List", list, "transfer-late-audit", ct);
                    return moved;
                }, value => value is not null, ct);
                throw new InvalidOperationException("Revoked audit insert succeeded.");
            }
            catch (PostgresException e) when (e.SqlState == "42501") { }
        }
        finally { await Sql("GRANT INSERT ON audit_events TO strataai_api_runtime;"); }
        Require(await Scalar(GraphHash(false)) == beforeRefusal, "Late audit failure did not restore the complete graph.");
        // A real competing owner holds the parent gate through the runtime's
        // normal deadline. The later success uses the exact original version/anchor.
        await using (var held = await admin.BeginTransactionAsync(ct))
        {
            await using var gate = new NpgsqlCommand($$"""SELECT id FROM boards WHERE id='{{source}}' FOR UPDATE;""", admin, held);
            await gate.ExecuteScalarAsync(ct);
            try { await Move(source, destination, 1, anchor); throw new InvalidOperationException("Held parent gate did not enforce the runtime deadline."); }
            catch (NpgsqlException e) when (e.InnerException is TimeoutException) { }
            await held.RollbackAsync(ct);
        }
        Require(await Scalar(GraphHash(false)) == beforeRefusal, "Timed-out parent wait changed its complete graph.");
        var stable = await Scalar(StableChildren()); var result = await Move(source, destination, 1, anchor);
        Require(result is { Cards.Count: 3 } && result.List.Id == list && result.List.Version == 2, "Complete lifecycle transfer failed.");
        Require(result!.Cards.Select(c => c.LifecycleState).Distinct().Count() == 3, "Transfer discarded a Card lifecycle.");
        Require(string.CompareOrdinal(result.List.Rank, new string('5', 30)) < 0, "Destination anchor ordering failed.");
        Require(await Scalar(StableChildren()) == stable, "Transfer rewrote stable content, children, history or lifecycle fields.");
        Require(await Scalar($$"""SELECT count(*)=3 AND bool_and(board_id='{{destination}}' AND version=2) FROM cards WHERE tenant_id='{{tenant}}' AND list_id='{{list}}';""") == "True", "Card parent/version update failed.");
        Require(await Scalar($$"""SELECT count(*)=1 FROM board_labels WHERE tenant_id='{{tenant}}' AND board_id='{{destination}}' AND status='ACTIVE';""") == "True", "Shared label was copied more than once.");
        Require(await Scalar($$"""SELECT count(*)=3 AND bool_and(a.board_id='{{destination}}' AND a.version=5 AND a.created_at=c.created_at) FROM card_labels a JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id WHERE a.tenant_id='{{tenant}}';""") == "True", "Label associations were lost or advanced incorrectly.");
        Require(await Scalar($$"""SELECT count(*)=3 AND bool_and(a.board_id='{{destination}}' AND a.user_id='{{member}}' AND a.assigned_by='{{actor}}' AND a.version=8 AND a.created_at=c.created_at) FROM card_members a JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id WHERE a.tenant_id='{{tenant}}';""") == "True", "Destination assignment reconciliation failed.");
        now = now.AddSeconds(1); Require(await Move(destination, source, 2) is not null, "Second movement failed.");
        if (Environment.GetEnvironmentVariable("STRATAAI_POST_MVP_LOAD_TESTS") != "1")
        {
            await using var scoped = await db.OpenTenantSessionAsync(foreign, ct);
            await using var q = new NpgsqlCommand("SELECT count(*) FROM cards", scoped.Connection, scoped.Transaction);
            Require((long)(await q.ExecuteScalarAsync(ct))! == 0, "Another tenant saw transferred Cards.");
            Console.WriteLine("Restricted List transfer functional cases passed; large-load acceptance is deferred until after the MVP.");
            return;
        }
        await Sql($$"""
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,created_at,updated_at)
              SELECT gen_random_uuid(),'{{tenant}}','{{source}}','{{list}}','Archived fixture',repeat('7',30),'ARCHIVED',now(),now(),now() FROM generate_series(1,100000);
            INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at)
              SELECT c.tenant_id,c.board_id,c.id,(SELECT a.label_id FROM card_labels a JOIN cards original ON original.tenant_id=a.tenant_id AND original.id=a.card_id
                WHERE original.tenant_id='{{tenant}}' AND original.lifecycle_state='ACTIVE' LIMIT 1),c.created_at,c.updated_at
              FROM cards c WHERE c.tenant_id='{{tenant}}' AND c.version=1;
            """);
        stable = await Scalar(StableChildren()); now = DateTimeOffset.UtcNow.AddMinutes(1);
        var large = await Move(source, destination, 3);
        Require(large is { Cards.Count: 100003 }, "Transfer truncated the large archived graph.");
        Require(await Scalar(StableChildren()) == stable, "Large transfer changed stable identities/content/children/lifecycle.");
        Require(await Scalar($$"""SELECT count(*)=100003 AND count(DISTINCT label_id)=1 AND bool_and(board_id='{{destination}}') FROM card_labels WHERE tenant_id='{{tenant}}';""") == "True", "Large shared-label mapping lost associations.");
        // Opposing transfers use independent real connections/command scopes;
        // the empty List is a separate unbound subject, not a truncated graph.
        var opposite = Guid.NewGuid(); now = DateTimeOffset.UtcNow.AddMinutes(1);
        await Sql($$"""
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
              VALUES('{{opposite}}','{{tenant}}','{{source}}','Opposing empty List',repeat('8',30),now(),now());
            """);
        async Task<(ListBoardTransfer? Value, bool TimedOut)> Observe(Task<ListBoardTransfer?> task)
        {
            try { return (await task, false); }
            catch (NpgsqlException e) when (e.InnerException is TimeoutException) { return (null, true); }
        }
        string LargeState() => $$"""
            SELECT md5(jsonb_build_object('list',(SELECT to_jsonb(l) FROM board_lists l WHERE id='{{list}}'),
              'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='{{tenant}}' AND list_id='{{list}}'),
              'labels',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_labels l WHERE tenant_id='{{tenant}}'),
              'associations',(SELECT jsonb_agg(to_jsonb(a) ORDER BY card_id,label_id) FROM card_labels a WHERE tenant_id='{{tenant}}'),
              'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY card_id,user_id) FROM card_members m WHERE tenant_id='{{tenant}}'))::text);
            """;
        var largeBeforeWait = await Scalar(LargeState());
        var emptyBeforeWait = await Scalar($$"""SELECT to_jsonb(l)::text FROM board_lists l WHERE id='{{opposite}}';""");
        Task<ListBoardTransfer?> EmptyMove() => db.ExecuteTenantCommandAsync(tenant,
            () => store.TransferListBoardAsync(tenant, source, opposite, destination, 1, now, true, null, ct), value => value is not null, ct);
        var opposing = await Task.WhenAll(Observe(Move(destination, source, 4)), Observe(EmptyMove()));
        var retriedWaits = opposing.Count(result => result.TimedOut);
        if (opposing[0].TimedOut)
        {
            Require(await Scalar(LargeState()) == largeBeforeWait, "Timed-out large transfer changed protected state.");
            opposing[0] = (await Move(destination, source, 4), false);
        }
        if (opposing[1].TimedOut)
        {
            Require(await Scalar($$"""SELECT to_jsonb(l)::text FROM board_lists l WHERE id='{{opposite}}';""") == emptyBeforeWait,
                "Timed-out waiting transfer changed its subject.");
            opposing[1] = (await EmptyMove(), false);
        }
        Require(opposing[0].Value is { Cards.Count: 100003 } && opposing[1].Value is { Cards.Count: 0 }, "Opposing transfers failed or truncated their graphs.");
        Require(await Scalar(StableChildren()) == stable, "Opposing transfer changed stable large-graph state.");
        await using (var scoped = await db.OpenTenantSessionAsync(foreign, ct))
        {
            await using var q = new NpgsqlCommand("SELECT count(*) FROM cards", scoped.Connection, scoped.Transaction);
            Require((long)(await q.ExecuteScalarAsync(ct))! == 0, "Another tenant saw transferred Cards.");
        }
        Console.WriteLine("Restricted List transfer persistence: all lifecycles, stable children, shared labels, destination members, late audit rollback, anchor ordering and 100,000 archived Cards passed.");
        Console.WriteLine($"Opposing transfer timeout recovery: {retriedWaits} unchanged owning operations retried with their original versions.");
        Console.WriteLine("Held parent deadline: complete unchanged refusal and exact original-version/anchor retry passed.");
    }
}
