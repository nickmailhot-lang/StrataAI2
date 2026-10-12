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
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;

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
        public Task<bool> VerifyAsync(RetentionHoldReference reference, Guid actor, CancellationToken ct) => Task.FromResult(Allowed);
    }
    private sealed class ChangedSource(IRetentionWorkSourceReferences inner) : IRetentionWorkSourceReferences
    {
        private int reads;
        public async Task<RetentionWorkSourceReference?> ReadAsync(Guid organization, RetentionRecordClass kind, Guid record, CancellationToken ct)
        {
            var value = await inner.ReadAsync(organization, kind, record, ct);
            return ++reads > 1 && value is not null ? value with { CardVersion = value.CardVersion + 1 } : value;
        }
    }
    // Explicit race control layered over the actual restricted producer reader.
    private sealed class ChangedProducer(IRetentionProducerReferences inner, int afterReads = 1) : IRetentionProducerReferences
    {
        private int reads;
        public async Task<RetentionProducerReference?> ReadAsync(Guid organization, RetentionProducerKind kind, Guid record, CancellationToken ct)
        {
            var value = await inner.ReadAsync(organization, kind, record, ct);
            return ++reads > afterReads && value is not null ? value with { SourceVersion = value.SourceVersion + 1 } : value;
        }
    }
    private sealed class BoardGate(IWorkBoardAuthorization inner, Guid? denied = null) : IWorkBoardAuthorization
    {
        public List<Guid> Gates { get; } = [];
        public Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid board, Guid? actor, CancellationToken ct = default)
        {
            Gates.Add(board);
            return board == denied ? Task.FromResult(WorkOperation<BoardSyncScope>.Failure("board_not_found")) : inner.GetSyncScopeAsync(board, actor, ct);
        }
    }
    private sealed class PrivateReadGuard(IRetentionHoldStore inner) : IRetentionHoldStore
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Task<RetentionHoldReference?> ReadReferenceAsync(Guid organization, Guid hold, CancellationToken ct) => inner.ReadReferenceAsync(organization, hold, ct);
        public Task<RetentionHoldReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptReferenceAsync(organization, actor, key, ct);
        public Task<RetentionHoldCandidates> ReadApplicableAsync(RetentionRecord record, CancellationToken ct) => throw new NotSupportedException();
        public Task<RetentionHold?> ReadAsync(Guid organization, Guid hold, CancellationToken ct) { Reads++; return inner.ReadAsync(organization, hold, ct); }
        public Task<RetentionHoldReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) { Reads++; return inner.ReadReceiptAsync(organization, actor, key, ct); }
        public Task<RetentionHoldWriteResult> WriteAsync(long expected, RetentionHoldWrite write, CancellationToken ct) { Writes++; return inner.WriteAsync(expected, write, ct); }
    }
    private sealed class AfterWrite(IRetentionHoldStore inner, Action after) : IRetentionHoldStore
    {
        public Task<RetentionHoldCandidates> ReadApplicableAsync(RetentionRecord record, CancellationToken ct) => inner.ReadApplicableAsync(record, ct);
        public Task<RetentionHoldReference?> ReadReferenceAsync(Guid organization, Guid hold, CancellationToken ct) => inner.ReadReferenceAsync(organization, hold, ct);
        public Task<RetentionHoldReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptReferenceAsync(organization, actor, key, ct);
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
        var board = Guid.NewGuid(); var destination = Guid.NewGuid(); var list = Guid.NewGuid(); var destinationList = Guid.NewGuid();
        var card = Guid.NewGuid(); var comment = Guid.NewGuid(); var attachment = Guid.NewGuid();
        var unclassified = Guid.NewGuid();
        var unclassifiedComment = Guid.NewGuid(); var unclassifiedAttachment = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        try
        {
            // Exercise canonical production hold capabilities without widening them.
            await using (var seed = new NpgsqlCommand("""
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
                INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
                VALUES(@board,@tenant,'Original Board',@at,@at),(@destination,@tenant,'Destination Board',@at,@at);
                INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
                VALUES(@list,@tenant,@board,'Original List','a',@at,@at),(@destinationList,@tenant,@destination,'Destination List','a',@at,@at);
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
                VALUES(@card,@tenant,@board,@list,'Private Card','a',@at,@at);
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
                VALUES(@unclassified,@tenant,@board,@list,'Unclassified Card','b',@at,@at);
                INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
                VALUES(@comment,@tenant,@card,@actor,'Private comment',@at,@at);
                INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
                VALUES(@unclassifiedComment,@tenant,@unclassified,@actor,'Private review comment',@at,@at);
                INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
                VALUES(@attachment,@tenant,@card,@actor,'URL','Private file','https://example.test/file','NOT_APPLICABLE',@at,@at);
                INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
                VALUES(@unclassifiedAttachment,@tenant,@unclassified,@actor,'URL','Private review file','https://example.test/review','NOT_APPLICABLE',@at,@at);
                INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,parent_card_id,category,registered_at,
                 actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
                SELECT @tenant,s.class,s.id,s.kind,s.parent,'private.context',@at,@actor,@member,1,'Native hold owner','ORGANIZATION_OWNER',gen_random_uuid(),'native-hold-contract'
                FROM (VALUES('OPERATIONAL_WORK',@card,'CARD',NULL::uuid),('OPERATIONAL_WORK',@comment,'CARD_COMMENT',@card),('DOCUMENT',@attachment,'ATTACHMENT',@card)) s(class,id,kind,parent);
                UPDATE cards SET board_id=@destination,list_id=@destinationList,version=2,updated_at=@at+interval '1 second' WHERE id=@card;
                SELECT set_config('app.tenant_id','',false),set_config('app.identity_subject','',false);
                """, admin))
            {
                seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", actor + "@example.test");
                seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("other", other);
                seed.Parameters.AddWithValue("member", member); seed.Parameters.AddWithValue("source", source); seed.Parameters.AddWithValue("at", at);
                seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("destination", destination);
                seed.Parameters.AddWithValue("list", list); seed.Parameters.AddWithValue("destinationList", destinationList);
                seed.Parameters.AddWithValue("card", card); seed.Parameters.AddWithValue("comment", comment); seed.Parameters.AddWithValue("attachment", attachment);
                seed.Parameters.AddWithValue("unclassified", unclassified);
                seed.Parameters.AddWithValue("unclassifiedComment", unclassifiedComment); seed.Parameters.AddWithValue("unclassifiedAttachment", unclassifiedAttachment);
                await seed.ExecuteNonQueryAsync(ct);
            }
            // Prove the narrow lock privilege separately from mutation permission.
            // Prove the exact lock prerequisite, then restore canonical version-only
            // permission. The immutable trigger still rejects every actual update.
            await using (var revokeSourceLock = new NpgsqlCommand("REVOKE UPDATE(version) ON record_retention_sources FROM strataai_api_runtime;", admin))
                await revokeSourceLock.ExecuteNonQueryAsync(ct);
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
            var references = new PostgresRetentionAuthorizationReferences(db);
            var producers = new PostgresRetentionProducerReferences(db);
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
                var raw = await producers.ReadAsync(tenant, RetentionProducerKind.Card, unclassified, ct);
                Require(raw is { ParentCardId: null, SourceVersion: 1, CardVersion: 1, ListVersion: 1, SourceState: StrataAI.Application.WorkManagement.WorkItemLifecycleState.Active }
                    && raw.CardId == unclassified && raw.BoardId == board && raw.ListId == list && raw.CreatedAt.Offset == TimeSpan.Zero
                    && await references.ReadSourceAsync(tenant, RetentionRecordClass.OperationalWork, unclassified, ct) is null,
                    "Native first-classification producer evidence depends on an existing registry or invents classification.");
                foreach (var item in new[] { (card, RetentionProducerKind.Card), (comment, RetentionProducerKind.CardComment), (attachment, RetentionProducerKind.Attachment) })
                {
                    var current = await producers.ReadAsync(tenant, item.Item2, item.Item1, ct);
                    Require(current is { CardVersion: 2 } && current.Kind == item.Item2 && current.CardId == card
                        && current.BoardId == destination && current.ListId == destinationList
                        && current.SourceVersion == (item.Item2 == RetentionProducerKind.Card ? 2 : 1)
                        && current.ParentCardId == (item.Item2 == RetentionProducerKind.Card ? null : card),
                        "Native raw producer substituted kind, current route, revision or parent.");
                }
                Require(await producers.ReadAsync(tenant, RetentionProducerKind.AuditEvent, source, ct) is null
                    && await producers.ReadAsync(tenant, RetentionProducerKind.Attachment, card, ct) is null
                    && await producers.ReadAsync(tenant, RetentionProducerKind.Card, Guid.NewGuid(), ct) is null,
                    "Native raw producer admitted unsupported, wrong-kind or unknown identity.");
                var candidates = await store.ReadApplicableAsync(record, ct);
                Require(candidates.ScopeComplete && candidates.Items.Single() == released.Value, "Native lookup omitted current release evidence.");
                Require(await store.ReadReceiptAsync(tenant, Guid.NewGuid(), key, ct) is null, "Native foreign actor receipt disclosed.");
                var auditHint = await references.ReadSourceAsync(tenant, RetentionRecordClass.Audit, source, ct);
                Require(auditHint is { Kind: RetentionProducerKind.AuditEvent, ContextVersion: null, OriginalBoardId: null, SourceVersion: null },
                    "Native Audit reference fabricated historical authority.");
                foreach (var item in new[] { (card, RetentionRecordClass.OperationalWork, RetentionProducerKind.Card),
                    (comment, RetentionRecordClass.OperationalWork, RetentionProducerKind.CardComment),
                    (attachment, RetentionRecordClass.Document, RetentionProducerKind.Attachment) })
                {
                    var hint = await references.ReadSourceAsync(tenant, item.Item2, item.Item1, ct);
                    Require(hint is { ContextVersion: 1, SourceVersion: 1 } && hint.Kind == item.Item3
                        && hint.RecordId == item.Item1 && hint.OriginalBoardId == board
                        && hint.ParentCardId == (item.Item3 == RetentionProducerKind.Card ? null : card),
                        "Native producer reference substituted destination context or parent.");
                }
                Require(await references.ReadSourceAsync(tenant, RetentionRecordClass.Document, card, ct) is null,
                    "Native reference admitted wrong record class.");
                Require(await references.ReadSourceAsync(tenant, RetentionRecordClass.OperationalWork, Guid.NewGuid(), ct) is null,
                    "Native unknown source reference admitted.");
                var headHint = await references.ReadHoldAsync(tenant, hold, ct);
                var receiptHint = await references.ReadReceiptAsync(tenant, actor, key, ct);
                Require(headHint is { Version: 2, CategoryScoped: true, ContextVersion: null } && headHint.RecordId == source
                    && receiptHint is { Version: 1, ContextVersion: null } && receiptHint.HoldId == hold,
                    "Native reference recovery substituted current head or invented context.");
                Require(await references.ReadReceiptAsync(tenant, Guid.NewGuid(), key, ct) is null,
                    "Native reference disclosed another actor's receipt.");
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
            try { await producers.ReadAsync(tenant, RetentionProducerKind.Card, card, ct); throw new Exception("Unowned raw producer IO admitted."); }
            catch (InvalidOperationException) { }
            try { await references.ReadHoldAsync(tenant, hold, ct); throw new Exception("Unowned reference IO admitted."); }
            catch (InvalidOperationException) { }
            var foreignReferences = await unit.ExecuteAsync(other, actor, null, false, async () =>
            {
                Require(await references.ReadSourceAsync(other, RetentionRecordClass.Audit, source, ct) is null
                    && await references.ReadHoldAsync(other, hold, ct) is null
                    && await references.ReadReceiptAsync(other, actor, key, ct) is null,
                    "Native reference crossed Organization boundary.");
                Require(await producers.ReadAsync(other, RetentionProducerKind.Card, unclassified, ct) is null,
                    "Native raw producer crossed Organization boundary.");
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(foreignReferences.Succeeded, "Native foreign reference fixture failed.");
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
            var workHold = Guid.NewGuid(); var workKey = Guid.NewGuid();
            Require((await service.ApplyAsync(tenant, actor, workHold,
                new(tenant, RetentionRecordClass.OperationalWork, null, card), "Private work reason", workKey, "native-reference", ct)).Succeeded,
                "Native stamped hold reference fixture failed.");
            var stamped = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
            {
                var head = await references.ReadHoldAsync(tenant, workHold, ct);
                var original = await references.ReadReceiptAsync(tenant, actor, workKey, ct);
                Require(head is { ContextVersion: 1, Version: 1, CategoryScoped: false, ApplicationSourceVersion: 2 }
                    && head.ClassificationBoardId == board && head.ApplicationBoardId == destination && head.RecordId == card
                    && original == head, "Native stamped hold reference lost original or application evidence.");
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(stamped.Succeeded, "Native stamped reference lookup unit failed.");
            var disposition = new PostgresRetentionWorkDisposition(db);
            async Task CheckDisposition(RetentionWorkDispositionKind kind, Guid recordId, RetentionDispositionAdmission expected)
            {
                var checkedScope = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
                {
                    Require(await disposition.CheckAsync(tenant, kind, recordId, ct) == expected,
                        "Native complete disposition scope disagreed with current hold/source family.");
                    return OrganizationOperation<bool>.Success(true);
                }, ct);
                Require(checkedScope.Succeeded, "Native owning disposition check failed.");
            }
            // More than 1,000 unrelated Audit holds are already present. Work
            // disposition must use complete EXISTS, not the bounded preview API.
            await CheckDisposition(RetentionWorkDispositionKind.Card, card, RetentionDispositionAdmission.Held);
            await CheckDisposition(RetentionWorkDispositionKind.List, destinationList, RetentionDispositionAdmission.Held);
            await CheckDisposition(RetentionWorkDispositionKind.Board, destination, RetentionDispositionAdmission.Held);
            await CheckDisposition(RetentionWorkDispositionKind.Board, board, RetentionDispositionAdmission.Clear);
            await CheckDisposition(RetentionWorkDispositionKind.CardComment, comment, RetentionDispositionAdmission.Clear);
            await CheckDisposition(RetentionWorkDispositionKind.Attachment, attachment, RetentionDispositionAdmission.Clear);
            var documentHold = Guid.NewGuid();
            Require((await service.ApplyAsync(tenant, actor, documentHold,
                new(tenant, RetentionRecordClass.Document, "PRIVATE.CONTEXT", attachment), "Private document reason", Guid.NewGuid(), "disposition-document", ct)).Succeeded,
                "Native disposition document hold fixture failed.");
            await CheckDisposition(RetentionWorkDispositionKind.Attachment, attachment, RetentionDispositionAdmission.Held);
            await CheckDisposition(RetentionWorkDispositionKind.CardComment, comment, RetentionDispositionAdmission.Clear);
            Require((await service.ReleaseAsync(tenant, actor, documentHold, 1, Guid.NewGuid(), "disposition-release", ct)).Succeeded,
                "Native disposition document release fixture failed.");
            await CheckDisposition(RetentionWorkDispositionKind.Attachment, attachment, RetentionDispositionAdmission.Clear);
            var categoryHold = Guid.NewGuid();
            Require((await service.ApplyAsync(tenant, actor, categoryHold,
                new(tenant, RetentionRecordClass.Document, "unknown.category", null), "Private category reason", Guid.NewGuid(), "disposition-category", ct)).Succeeded,
                "Native disposition category hold fixture failed.");
            await CheckDisposition(RetentionWorkDispositionKind.Attachment, attachment, RetentionDispositionAdmission.Clear);
            await CheckDisposition(RetentionWorkDispositionKind.Attachment, unclassifiedAttachment, RetentionDispositionAdmission.Held);
            await CheckDisposition(RetentionWorkDispositionKind.Card, unclassified, RetentionDispositionAdmission.Held);
            await CheckDisposition(RetentionWorkDispositionKind.List, list, RetentionDispositionAdmission.Held);
            await CheckDisposition(RetentionWorkDispositionKind.Board, board, RetentionDispositionAdmission.Held);
            Require((await service.ReleaseAsync(tenant, actor, categoryHold, 1, Guid.NewGuid(), "disposition-category-release", ct)).Succeeded,
                "Native disposition category release fixture failed.");
            await CheckDisposition(RetentionWorkDispositionKind.Board, board, RetentionDispositionAdmission.Clear);
            var organizationHold = Guid.NewGuid();
            Require((await service.ApplyAsync(tenant, actor, organizationHold,
                new(tenant, null, null, null), "Private Organization reason", Guid.NewGuid(), "disposition-organization", ct)).Succeeded,
                "Native disposition Organization hold fixture failed.");
            await CheckDisposition(RetentionWorkDispositionKind.CardComment, comment, RetentionDispositionAdmission.Held);
            Require((await service.ReleaseAsync(tenant, actor, organizationHold, 1, Guid.NewGuid(), "disposition-organization-release", ct)).Succeeded,
                "Native disposition Organization release fixture failed.");
            await CheckDisposition(RetentionWorkDispositionKind.CardComment, comment, RetentionDispositionAdmission.Clear);
            await CheckDisposition(RetentionWorkDispositionKind.Card, Guid.NewGuid(), RetentionDispositionAdmission.Unavailable);
            var foreignDisposition = await unit.ExecuteAsync(other, actor, null, false, async () =>
            {
                Require(await disposition.CheckAsync(other, RetentionWorkDispositionKind.Card, card, ct) == RetentionDispositionAdmission.Unavailable,
                    "Native disposition crossed tenant scope.");
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(foreignDisposition.Succeeded, "Native foreign disposition fixture failed.");
            try { await disposition.CheckAsync(tenant, RetentionWorkDispositionKind.Card, card, ct); throw new Exception("Unowned disposition IO admitted."); }
            catch (InvalidOperationException) { }
            Console.WriteLine("Native complete Work disposition: current moved families, record/category/Organization holds, releases, unknown classification, tenant refusal and unowned refusal passed.");
            var workStore = new PostgresWorkManagementStore(db);
            // Only the existing GetSyncScopeAsync admission path is used. Its
            // event/notification/reminder mutation collaborators are not invoked.
            IWorkBoardAuthorization actualBoards = new WorkManagementService(workStore, organizations, clock,
                null!, policy, null!, null!, null!, null!, new PostgresRetentionWorkDisposition(db));
            var review = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
            {
                var auth = new RetentionProducerAuthorization(producers, workStore, actualBoards, organizations, actors);
                foreach (var item in new[] { (unclassified, RetentionProducerKind.Card), (card, RetentionProducerKind.Card),
                    (comment, RetentionProducerKind.CardComment), (attachment, RetentionProducerKind.Attachment) })
                {
                    var evidence = (await producers.ReadAsync(tenant, item.Item2, item.Item1, ct))!;
                    Require(await auth.VerifyAsync(evidence, actor, ct), "Native actual first-classification review admission failed.");
                    Require(!await auth.VerifyAsync(evidence, Guid.NewGuid(), ct)
                        && !await auth.VerifyAsync(evidence with { SourceVersion = 99 }, actor, ct)
                        && !await auth.VerifyAsync(evidence with { Kind = RetentionProducerKind.AuditEvent }, actor, ct)
                        && !await auth.VerifyAsync(evidence with { BoardId = Guid.NewGuid() }, actor, ct),
                        "Native review admitted foreign actor, wrong revision/kind or forged route.");
                    var denied = new RetentionProducerAuthorization(producers, workStore,
                        new BoardGate(actualBoards, evidence.BoardId), organizations, actors);
                    Require(!await denied.VerifyAsync(evidence, actor, ct), "Native first review bypassed actual Board refusal.");
                    var changed = new RetentionProducerAuthorization(new ChangedProducer(producers), workStore, actualBoards, organizations, actors);
                    Require(!await changed.VerifyAsync(evidence, actor, ct), "Native review adopted changed producer evidence after admission.");
                }
                Require(await references.ReadSourceAsync(tenant, RetentionRecordClass.OperationalWork, unclassified, ct) is null,
                    "Native review invented a classification while admitting source evidence.");
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(review.Succeeded, "Native first-classification review unit failed.");
            async Task<bool> Authorize(Func<RetentionWorkAuthorization, Task<bool>> action,
                IWorkBoardAuthorization? boardAuthorization = null, IRetentionWorkSourceReferences? sourceReferences = null)
            {
                var result = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
                    OrganizationOperation<bool>.Success(await action(new(references, sourceReferences ?? references,
                        workStore, boardAuthorization ?? actualBoards, organizations, actors))), ct);
                Require(result.Succeeded, "Native retained source authorization fixture failed.");
                return result.Value;
            }
            foreach (var item in new[] { (card, RetentionRecordClass.OperationalWork), (comment, RetentionRecordClass.OperationalWork), (attachment, RetentionRecordClass.Document) })
                Require(await Authorize(a => a.VerifySourceAsync(tenant, item.Item2, item.Item1, actor, ct)),
                    "Native actual retained producer admission failed.");
            Require(!await Authorize(a => a.VerifySourceAsync(tenant, RetentionRecordClass.Audit, source, actor, ct)),
                "Native unresolved Audit origin gained work authority.");
            Require(!await Authorize(a => a.VerifySourceAsync(tenant, RetentionRecordClass.OperationalWork, card, Guid.NewGuid(), ct)),
                "Native retained source authority admitted a nonmember.");
            Require(!await Authorize(async a => await a.VerifyHoldAsync((await references.ReadHoldAsync(tenant, workHold, ct))!
                with { RecordId = null, ContextVersion = null, ClassificationBoardId = null, ApplicationBoardId = null, ApplicationSourceVersion = null }, actor, ct)),
                "Native work-record authority substituted for broad retention administration.");
            var gatePlan = new BoardGate(actualBoards);
            Require(await Authorize(a => a.VerifySourceAsync(tenant, RetentionRecordClass.OperationalWork, card, actor, ct), gatePlan)
                && gatePlan.Gates.SequenceEqual(new[] { board, destination }.Order()),
                "Native original/current Board plan was incomplete or unordered.");
            Require(!await Authorize(a => a.VerifySourceAsync(tenant, RetentionRecordClass.OperationalWork, card, actor, ct), new BoardGate(actualBoards, board)),
                "Native historical Board refusal was bypassed by destination access.");
            Require(!await Authorize(a => a.VerifySourceAsync(tenant, RetentionRecordClass.OperationalWork, card, actor, ct), new BoardGate(actualBoards, destination)),
                "Native current Board refusal was bypassed by historical access.");
            Require(!await Authorize(a => a.VerifySourceAsync(tenant, RetentionRecordClass.OperationalWork, card, actor, ct),
                sourceReferences: new ChangedSource(references)), "Native changed source revision passed its final fence.");
            await using (var archive = new NpgsqlCommand("""
                UPDATE boards SET lifecycle_state='ARCHIVED',archived_at=statement_timestamp(),updated_at=statement_timestamp(),version=version+1 WHERE id=@board;
                UPDATE cards SET board_id=@board,list_id=@list,lifecycle_state='ARCHIVED',archived_at=statement_timestamp(),updated_at=statement_timestamp(),version=version+1 WHERE id=@card;
                """, admin))
            {
                archive.Parameters.AddWithValue("board", board); archive.Parameters.AddWithValue("card", card);
                archive.Parameters.AddWithValue("list", list);
                await archive.ExecuteNonQueryAsync(ct);
            }
            var archivedReview = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
            {
                var evidence = (await producers.ReadAsync(tenant, RetentionProducerKind.Card, card, ct))!;
                return OrganizationOperation<bool>.Success(await new RetentionProducerAuthorization(producers, workStore,
                    actualBoards, organizations, actors).VerifyAsync(evidence, actor, ct));
            }, ct);
            Require(archivedReview.Succeeded && archivedReview.Value, "Native archived producer review lost its actual administrator.");
            Require(await Authorize(a => a.VerifySourceAsync(tenant, RetentionRecordClass.OperationalWork, card, actor, ct)),
                "Native retained archived source became inaccessible to its current administrator.");
            Require(await Authorize(async a => await a.VerifyHoldAsync((await references.ReadHoldAsync(tenant, workHold, ct))!, actor, ct)),
                "Native retained hold application gates failed after archive.");
            Require(!await Authorize(async a => await a.VerifyHoldAsync((await references.ReadHoldAsync(tenant, workHold, ct))!, actor, ct),
                new BoardGate(actualBoards, destination)), "Native application Board refusal was bypassed after a later source move.");
            var realAuthorization = new RetentionWorkAuthorization(references, references, workStore, actualBoards, organizations, actors);
            var administrativeAuthority = new RetentionHoldAuthorization(realAuthorization,
                new RetentionAdministrationAuthorization(organizations, actors, new PostgresRetentionAdministrationTransaction(db)));
            var administrativeStore = new PostgresRetentionHoldStore(db, organizations, identities, actors, administrativeAuthority, clock);
            var administrativeService = new RetentionHoldService(organizations, administrativeStore, administrativeAuthority, unit, actors, clock);
            var organizationDisposition = new PostgresRetentionOrganizationDisposition(db);
            Require(await organizationDisposition.CheckAsync(tenant, ct) == RetentionDispositionAdmission.Unavailable,
                "Native Organization disposition admitted an unowned scope.");
            var offboardingProbe = Guid.NewGuid();
            var createdProbe = await unit.ExecuteAsync(offboardingProbe, actor, null, true, async () =>
            {
                await organizations.CreateOrganizationAsync(actor, offboardingProbe, "Native offboarding probe", null, clock.UtcNow, ct);
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(createdProbe.Succeeded, "Native owning offboarding probe creation failed.");
            async Task ProveMinimumOrganizationDisposition(Guid organization, RetentionDispositionAdmission expected)
            {
                await using (var minimum = new NpgsqlCommand("""
                    REVOKE SELECT,INSERT,UPDATE ON record_retention_holds FROM strataai_api_runtime;
                    REVOKE SELECT(id,version),UPDATE(version,status,released_by,released_at) ON record_retention_holds FROM strataai_api_runtime;
                    GRANT SELECT(tenant_id,record_class,category_key,record_id,status) ON record_retention_holds TO strataai_api_runtime;
                    REVOKE SELECT,INSERT ON record_retention_hold_history,record_retention_hold_receipts FROM strataai_api_runtime;
                    """, admin)) await minimum.ExecuteNonQueryAsync(ct);
                try
                {
                    var checkedRoot = await unit.ExecuteAsync(organization, actor, null, false, async () =>
                    {
                        Require(await organizationDisposition.CheckAsync(organization, ct) == expected,
                            "Native current Organization hold admission differs from its complete scope.");
                        Require(await organizationDisposition.CheckAsync(other, ct) == RetentionDispositionAdmission.Unavailable,
                            "Native Organization disposition accepted a foreign owning scope.");
                        return OrganizationOperation<bool>.Success(true);
                    }, ct);
                    Require(checkedRoot.Succeeded, "Native minimum-capability Organization disposition failed.");
                }
                finally
                {
                    await using var prospective = new NpgsqlCommand("""
                        GRANT SELECT,INSERT ON record_retention_holds TO strataai_api_runtime;
                        GRANT UPDATE(version,status,released_by,released_at) ON record_retention_holds TO strataai_api_runtime;
                        GRANT SELECT,INSERT ON record_retention_hold_history,record_retention_hold_receipts TO strataai_api_runtime;
                        """, admin);
                    await prospective.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            await ProveMinimumOrganizationDisposition(tenant, RetentionDispositionAdmission.Held);
            await ProveMinimumOrganizationDisposition(offboardingProbe, RetentionDispositionAdmission.Clear);
            var offboardingHold = Guid.NewGuid();
            Require((await administrativeService.ApplyAsync(offboardingProbe, actor, offboardingHold,
                new(offboardingProbe, RetentionRecordClass.Governance, "native.future", null),
                "Private offboarding hold", Guid.NewGuid(), "native-offboarding-hold", ct)).Succeeded,
                "Native offboarding hold application failed.");
            await ProveMinimumOrganizationDisposition(offboardingProbe, RetentionDispositionAdmission.Held);
            Require((await administrativeService.ReleaseAsync(offboardingProbe, actor, offboardingHold, 1,
                Guid.NewGuid(), "native-offboarding-release", ct)).Succeeded, "Native offboarding hold release failed.");
            await ProveMinimumOrganizationDisposition(offboardingProbe, RetentionDispositionAdmission.Clear);
            Console.WriteLine("Native Organization disposition: minimum canonical scope columns, no private hold/history/receipt read, complete active holds, release and foreign/unowned refusal passed; actor session remains a native fixture.");
            Require(!await administrativeAuthority.VerifyAsync(new RetentionHoldScope(tenant, null, null, null), actor, ct),
                "Native broad administration admitted an unowned transaction.");
            foreach (var broadScope in new[] { new RetentionHoldScope(tenant, null, null, null),
                new(tenant, RetentionRecordClass.Document, null, null),
                new(tenant, RetentionRecordClass.Document, "native.administration", null) })
            {
                var broadId = Guid.NewGuid(); var broadKey = Guid.NewGuid(); var releaseKey = Guid.NewGuid();
                var appliedBroad = await administrativeService.ApplyAsync(tenant, actor, broadId, broadScope,
                    "Private administrative hold reason", broadKey, "native-broad-apply", ct);
                Require(appliedBroad.Succeeded && appliedBroad.Value is { Version: 1, Status: RetentionHoldStatus.Active },
                    "Native actual broad hold administration apply failed.");
                Require((await administrativeService.ReadAsync(tenant, actor, broadId, ct)).Value == appliedBroad.Value,
                    "Native broad private read did not retain owning administration.");
                var releasedBroad = await administrativeService.ReleaseAsync(tenant, actor, broadId, 1, releaseKey, "native-broad-release", ct);
                Require(releasedBroad.Succeeded && releasedBroad.Value is { Version: 2, Status: RetentionHoldStatus.Released },
                    "Native actual broad hold administration release failed.");
                Require((await administrativeService.ApplyAsync(tenant, actor, broadId, broadScope,
                    "Private administrative hold reason", broadKey, "native-broad-original-apply", ct)).Value == appliedBroad.Value
                    && (await administrativeService.ReleaseAsync(tenant, actor, broadId, 1, releaseKey,
                        "native-broad-original-release", ct)).Value == releasedBroad.Value,
                    "Native actual broad administration lost immutable original acknowledgments.");
            }
            Console.WriteLine("Native broad hold administration: actual owning Organization/class/category authority, private reads, release and original receipt recovery passed; actor session remains a native fixture.");
            var realStore = new PostgresRetentionHoldStore(db, organizations, identities, actors, realAuthorization, clock);
            var realService = new RetentionHoldService(organizations, realStore, realAuthorization, unit, actors, clock);
            Require((await realService.ReadAsync(tenant, actor, workHold, ct)).Succeeded, "Native real reference-admitted private read failed.");
            var releasedWork = await realService.ReleaseAsync(tenant, actor, workHold, 1, Guid.NewGuid(), "real-reference-release", ct);
            Require(releasedWork.Succeeded && releasedWork.Value is { Version: 2, Status: RetentionHoldStatus.Released },
                "Native real reference-admitted release failed.");
            Require((await realService.ApplyAsync(tenant, actor, workHold, new(tenant, RetentionRecordClass.OperationalWork, null, card),
                "Private work reason", workKey, "real-original-recovery", ct)).Value is { Version: 1, Status: RetentionHoldStatus.Active },
                "Native real admission lost original Apply receipt history.");
            var deniedAuthorization = new RetentionWorkAuthorization(references, references, workStore,
                new BoardGate(actualBoards, destination), organizations, actors);
            var privateGuard = new PrivateReadGuard(realStore);
            var deniedService = new RetentionHoldService(organizations, privateGuard, deniedAuthorization, unit, actors, clock);
            Require((await deniedService.ReadAsync(tenant, actor, workHold, ct)).ErrorCode == "retention_hold_not_found"
                && (await deniedService.ReleaseAsync(tenant, actor, workHold, 999, Guid.NewGuid(), "private-stale", ct)).ErrorCode == "retention_hold_not_found"
                && (await deniedService.ApplyAsync(tenant, actor, workHold, new(tenant, RetentionRecordClass.OperationalWork, null, card),
                    "Private work reason", workKey, "private-retry", ct)).ErrorCode == "retention_hold_not_found"
                && privateGuard.Reads == 0 && privateGuard.Writes == 0,
                "Native application gate denial occurred after private body reads or disclosed a revision conflict.");
            // Classification capabilities are canonical and remain immutable.
            await using (var grantSource = new NpgsqlCommand("GRANT INSERT ON record_retention_sources TO strataai_api_runtime; GRANT SELECT,INSERT ON record_retention_source_receipts TO strataai_api_runtime;", admin))
                await grantSource.ExecuteNonQueryAsync(ct);
            var sourceAuthorization = new RetentionProducerAuthorization(producers, workStore, actualBoards, organizations, actors);
            var sourceStore = new PostgresRetentionSourceStore(db, organizations, identities, actors, sourceAuthorization, clock);
            var reviewCommand = new RetentionSourceReviewService(producers, sourceStore, sourceAuthorization, organizations, actors, unit);
            var sourceEvents = new List<Guid>();
            foreach (var item in new[] { (unclassified, RetentionProducerKind.Card, RetentionRecordClass.OperationalWork),
                (unclassifiedComment, RetentionProducerKind.CardComment, RetentionRecordClass.OperationalWork),
                (unclassifiedAttachment, RetentionProducerKind.Attachment, RetentionRecordClass.Document) })
            {
                var reviewed = await reviewCommand.ReviewAsync(tenant, actor, item.Item2, item.Item1, ct);
                Require(reviewed.Succeeded && reviewed.Value is { SourceVersion: 1 } && reviewed.Value.RecordId == item.Item1
                    && reviewed.Value.Kind == item.Item2 && reviewed.Value.BoardId == board,
                    "Native owning review did not return the actual unclassified producer.");
                var lateReader = new ChangedProducer(producers, 3);
                var racingReview = new RetentionSourceReviewService(lateReader, sourceStore,
                    new RetentionProducerAuthorization(lateReader, workStore, actualBoards, organizations, actors), organizations, actors, unit);
                var withdrawnReview = await racingReview.ReviewAsync(tenant, actor, item.Item2, item.Item1, ct);
                Require(!withdrawnReview.Succeeded && withdrawnReview.Value is null,
                    "Native review returned evidence changed after its first Board gate.");
                var eventId = Guid.NewGuid(); var classificationKey = Guid.NewGuid();
                async Task<OrganizationOperation<bool>> Classify(bool rollBack, PostgresRetentionSourceStore adapter)
                    => await unit.ExecuteAsync(tenant, actor, null, false, async () =>
                    {
                        var evidence = reviewed.Value!;
                        var membership = (await organizations.FindMembershipAsync(tenant, actor, ct))!;
                        var now = clock.UtcNow; now = now.AddTicks(-(now.Ticks % 10));
                        var write = new RetentionSourceWrite(tenant, item.Item3, item.Item1, item.Item2, "native.review", "explicit native jurisdiction",
                            evidence.SourceVersion, evidence.CardVersion, actor, membership.Id, membership.Version, membership.Role,
                            eventId, "native-classification", now, evidence, classificationKey, new string('A', 64));
                        Require(await adapter.CaptureAsync(write with { MembershipId = Guid.NewGuid() }, ct) is null
                            && await adapter.CaptureAsync(write with { Review = evidence with { BoardId = destination } }, ct) is null
                            && await adapter.CaptureAsync(write with { Kind = RetentionProducerKind.AuditEvent }, ct) is null,
                            "Native classification admitted forged membership, review route or producer kind.");
                        var captured = await adapter.CaptureAsync(write, ct);
                        Require(captured is { ContextVersion: 1, SourceVersion: 1 } && captured.RecordId == item.Item1
                            && captured.Kind == item.Item2 && captured.OriginalBoardId == board && captured.ParentCardId == evidence.ParentCardId,
                            "Native classification lost actual reviewed context or typed parent.");
                        Require(await adapter.CaptureAsync(write with { Category = "rewritten", EventId = Guid.NewGuid() }, ct) is null,
                            "Native existing classification was rewritten.");
                        var receipt = await adapter.ReadReceiptAsync(tenant, actor, classificationKey, ct);
                        Require(receipt is not null && receipt.Result.Reference == captured && receipt.Result.EventId == eventId
                            && receipt.Result.Category == write.Category && receipt.Fingerprint == write.Fingerprint
                            && receipt.ExpiresAt == now.AddHours(24), "Native adapter lost original receipt graph.");
                        return rollBack ? OrganizationOperation<bool>.Failure("native_classification_rollback") : OrganizationOperation<bool>.Success(true);
                    }, ct);
                Require(!(await Classify(true, sourceStore)).Succeeded, "Native classification rollback fixture committed.");
                var absent = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
                    OrganizationOperation<bool>.Success(await sourceStore.ReadSourceAsync(tenant, item.Item3, item.Item1, ct) is null
                        && await sourceStore.ReadReceiptAsync(tenant, actor, classificationKey, ct) is null), ct);
                Require(absent.Succeeded && absent.Value, "Native classification survived owning rollback.");
                // Fail the actual storage adapter's post-insert review fence. The
                // source and trigger-owned audit must roll back to its savepoint.
                var late = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
                {
                    var evidence = (await producers.ReadAsync(tenant, item.Item2, item.Item1, ct))!;
                    var membership = (await organizations.FindMembershipAsync(tenant, actor, ct))!;
                    var now = clock.UtcNow; now = now.AddTicks(-(now.Ticks % 10));
                    var adapter = new PostgresRetentionSourceStore(db, organizations, identities, actors,
                        new RetentionProducerAuthorization(new ChangedProducer(producers, 2), workStore, actualBoards, organizations, actors), clock);
                    Require(await adapter.CaptureAsync(new(tenant, item.Item3, item.Item1, item.Item2, "native.review", null,
                        evidence.SourceVersion, evidence.CardVersion, actor, membership.Id, membership.Version, membership.Role,
                        eventId, "native-late-classification", now, evidence, classificationKey, new string('A', 64)), ct) is null
                        && await sourceStore.ReadSourceAsync(tenant, item.Item3, item.Item1, ct) is null
                        && await sourceStore.ReadReceiptAsync(tenant, actor, classificationKey, ct) is null,
                        "Native late review withdrawal retained a classification.");
                    return OrganizationOperation<bool>.Success(true);
                }, ct);
                Require(late.Succeeded, "Native classification savepoint did not reclaim source/audit/receipt identity.");
                await using (var rolledBackAudit = new NpgsqlCommand("SELECT count(*) FROM audit_events WHERE id=@event;", admin))
                {
                    rolledBackAudit.Parameters.AddWithValue("event", eventId);
                    Require(Convert.ToInt64(await rolledBackAudit.ExecuteScalarAsync(ct)) == 0, "Native classification rollback retained its original safe audit.");
                }
                var owningClassification = new RetentionSourceClassificationService(sourceStore, sourceAuthorization,
                    new RetentionWorkAuthorization(sourceStore, references, workStore, actualBoards, organizations, actors), organizations, actors, unit, clock);
                var committed = await owningClassification.ClassifyAsync(tenant, actor, reviewed.Value!, "native.review", "explicit native jurisdiction", classificationKey, "native-owning-classify", ct);
                Require(committed.Succeeded && committed.Value is not null, "Native owning first-classification command failed.");
                var holdReviews = new RetentionHoldReviewService(producers, sourceAuthorization,
                    new RetentionWorkAuthorization(sourceStore, references, workStore, actualBoards, organizations, actors),
                    organizations, actors, unit);
                var currentHoldReview = await holdReviews.ReviewAsync(tenant, actor, item.Item2, item.Item1, ct);
                Require(currentHoldReview.Succeeded && currentHoldReview.Value == reviewed.Value,
                    "Native classified hold review lost current producer evidence or owning authority.");
                var reviewedHold = Guid.NewGuid(); var reviewedHoldKey = Guid.NewGuid();
                var reviewedHoldScope = new RetentionHoldScope(tenant, item.Item3, "native.review", item.Item1);
                Require((await realService.ApplyReviewedAsync(tenant, actor, reviewedHold, reviewedHoldScope,
                    "Private reviewed native hold", reviewed.Value! with { SourceVersion = reviewed.Value!.SourceVersion + 1 },
                    sourceAuthorization, reviewedHoldKey, "native-stale-hold-review", ct)).ErrorCode == "retention_hold_review_changed",
                    "Native reviewed hold admitted stale source evidence.");
                var reviewedApplied = await realService.ApplyReviewedAsync(tenant, actor, reviewedHold, reviewedHoldScope,
                    "Private reviewed native hold", reviewed.Value!, sourceAuthorization, reviewedHoldKey, "native-reviewed-hold", ct);
                Require(reviewedApplied.Succeeded, "Native current reviewed hold application failed.");
                Require((await realService.ApplyReviewedAsync(tenant, actor, reviewedHold, reviewedHoldScope,
                    "Private reviewed native hold", reviewed.Value!, sourceAuthorization, reviewedHoldKey,
                    "native-reviewed-hold-recovery", ct)).Value == reviewedApplied.Value,
                    "Native reviewed hold lost original acknowledgment.");
                Require((await realService.ApplyReviewedAsync(tenant, actor, reviewedHold, reviewedHoldScope,
                    "Private reviewed native hold", reviewed.Value! with { CardVersion = reviewed.Value!.CardVersion + 1 },
                    sourceAuthorization, reviewedHoldKey, "native-reviewed-key-conflict", ct)).ErrorCode == "idempotency_key_conflict",
                    "Native reviewed hold reused a key for altered evidence.");
                Require((await realService.ReleaseAsync(tenant, actor, reviewedHold, 1,
                    Guid.NewGuid(), "native-reviewed-hold-release", ct)).Succeeded, "Native reviewed hold release failed.");
                sourceEvents.Add(committed.Value!.EventId);
                var replay = await owningClassification.ClassifyAsync(tenant, actor, reviewed.Value!, "native.review", "explicit native jurisdiction", classificationKey, "native-owning-replay", ct);
                Require(replay.Succeeded && replay.Value == committed.Value, "Native owning classification did not recover its original result.");
                Require((await owningClassification.ClassifyAsync(tenant, actor, reviewed.Value!, "conflicting", "explicit native jurisdiction", classificationKey, "native-owning-conflict", ct)).ErrorCode == "idempotency_key_conflict"
                    && (await owningClassification.ClassifyAsync(tenant, actor, reviewed.Value!, "native.review", null, Guid.NewGuid(), "native-owning-new-key", ct)).ErrorCode == "retention_source_unavailable",
                    "Native owning classification accepted changed input or a new-key rewrite.");
                var classifiedReview = await reviewCommand.ReviewAsync(tenant, actor, item.Item2, item.Item1, ct);
                Require(!classifiedReview.Succeeded && classifiedReview.Value is null && classifiedReview.ErrorCode == "retention_source_unavailable",
                    "Native first-classification review disclosed existing classification or allowed rewriting.");
            }
            await using (var auditSource = new NpgsqlCommand("""
                SELECT count(*) FROM audit_events WHERE id=ANY(@events) AND tenant_id=@tenant
                  AND event_type='RECORD_RETENTION_SOURCE_REGISTERED'
                  AND (SELECT count(*) FROM jsonb_object_keys(safe_metadata))=3
                  AND safe_metadata ?& ARRAY['recordClass','sourceKind','actingCapacity'];
                """, admin))
            {
                auditSource.Parameters.AddWithValue("events", sourceEvents.ToArray()); auditSource.Parameters.AddWithValue("tenant", tenant);
                Require(Convert.ToInt64(await auditSource.ExecuteScalarAsync(ct)) == 3, "Native classification audit privacy or atomic identity failed.");
            }
            try { await sourceStore.ReadSourceAsync(tenant, RetentionRecordClass.OperationalWork, unclassified, ct); throw new Exception("Unowned classification IO admitted."); }
            catch (InvalidOperationException) { }
            var raceHold = Guid.NewGuid();
            Require((await realService.ApplyAsync(tenant, actor, raceHold,
                new(tenant, RetentionRecordClass.OperationalWork, null, unclassified), "Private ordered hold", Guid.NewGuid(), "ordered-apply", ct)).Succeeded,
                "Native ordered hold application failed.");
            var workUnit = new PostgresWorkManagementUnitOfWork(db, actors, NullLogger<PostgresWorkManagementUnitOfWork>.Instance);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var retainedCheck = Task.Run(() => workUnit.ExecuteReadAsync(tenant, actor, "board_not_found",
                () => workStore.AcquireBoardReadScopeAsync(tenant, actor, board, ct), async () =>
                {
                    Require(await disposition.CheckAsync(tenant, RetentionWorkDispositionKind.Card, unclassified, ct) == RetentionDispositionAdmission.Held,
                        "Native Work gate did not observe the committed active hold.");
                    entered.TrySetResult();
                    await finish.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
                    Require(await disposition.CheckAsync(tenant, RetentionWorkDispositionKind.Card, unclassified, ct) == RetentionDispositionAdmission.Held,
                        "Native hold changed before the owning Work gate finished.");
                    return WorkOperation<bool>.Success(true);
                }, ct), ct);
            Task<OrganizationOperation<RetentionHold>>? competingRelease = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                competingRelease = Task.Run(() => realService.ReleaseAsync(tenant, actor, raceHold, 1, Guid.NewGuid(), "ordered-release", ct), ct);
                var waiting = false; var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
                while (!waiting && DateTimeOffset.UtcNow < deadline)
                {
                    await using var blocked = new NpgsqlCommand("""
                        SELECT EXISTS(SELECT FROM pg_stat_activity WHERE usename='strataai_api_runtime'
                          AND wait_event_type='Lock' AND position('organizations' IN query)>0 AND position('FOR UPDATE' IN query)>0);
                        """, admin);
                    waiting = await blocked.ExecuteScalarAsync(ct) is true;
                    if (!waiting) await Task.Delay(25, ct);
                }
                Require(waiting && !competingRelease.IsCompleted,
                    "Native competing hold release did not wait on the retained Organization gate.");
            }
            finally
            {
                finish.TrySetResult();
                Require((await retainedCheck).Succeeded, "Native retained Work transaction failed.");
                if (competingRelease is not null)
                    Require((await competingRelease).Succeeded, "Native waiting hold release failed after Work completion.");
            }
            await CheckDisposition(RetentionWorkDispositionKind.Card, unclassified, RetentionDispositionAdmission.Clear);
            Console.WriteLine("Native hold ordering: real Work SHARE/Board gate retained current hold until completion; competing authorized release waited on PostgreSQL and then committed.");
            Console.WriteLine("Native reviewed holds: actual Card/Comment/Attachment evidence, stale refusal, immutable original recovery, changed-review key refusal and release passed; actor session remains a native fixture.");
            Console.WriteLine("Native hold and source adapters: restricted owning persistence, original recovery, bounded lookup, reviewed classifications, rollback and safe audit passed; explicit denial/race controls remain fixtures.");
        }
        finally
        {
            await using var restore = new NpgsqlCommand("""
                REVOKE SELECT,INSERT,UPDATE ON record_retention_holds FROM strataai_api_runtime;
                REVOKE SELECT(id,version),UPDATE(version,status,released_by,released_at) ON record_retention_holds FROM strataai_api_runtime;
                GRANT SELECT,INSERT ON record_retention_holds TO strataai_api_runtime;
                GRANT UPDATE(version,status,released_by,released_at) ON record_retention_holds TO strataai_api_runtime;
                GRANT SELECT,INSERT ON record_retention_hold_history,record_retention_hold_receipts TO strataai_api_runtime;
                GRANT UPDATE(version) ON record_retention_sources TO strataai_api_runtime;
                """, admin);
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
