using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Actual restricted Worker stages, audit/outbox, continuations and terminal
// delivery. Raw fixture source metadata is not physical provider erasure proof.
internal static class OrganizationDeletionPagesContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant=Guid.NewGuid(); var actor=Guid.NewGuid(); var historic=Guid.NewGuid(); var request=Guid.NewGuid();
        void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
        async Task Admin(string sql)
        {
            await using var command=new NpgsqlCommand(sql,admin);
            command.Parameters.AddWithValue("tenant",tenant); command.Parameters.AddWithValue("actor",actor);
            command.Parameters.AddWithValue("historic",historic); command.Parameters.AddWithValue("request",request);
            await command.ExecuteNonQueryAsync(ct);
        }
        async Task<string> Fingerprint(string sql)
        {
            await using var query=new NpgsqlCommand(sql,admin); query.Parameters.AddWithValue("tenant",tenant);
            return (string)(await query.ExecuteScalarAsync(ct))!;
        }
        await Admin("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             SELECT id,'page-'||id||'@example.test',upper('page-'||id||'@example.test'),'Page fixture','ACTIVE','unused',now(),now()
             FROM unnest(ARRAY[@actor,@historic]::uuid[]) id;
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Page fixture',@actor,'DELETING',2,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE'),(gen_random_uuid(),@tenant,@historic,'MEMBER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,lifecycle_state,archived_at,deleted_at,deleted_by,created_at,updated_at)
             SELECT md5(@tenant::text||':board:'||n)::uuid,@tenant,'Page Board',
              CASE n WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,
              CASE WHEN n>=2 THEN now() END,CASE WHEN n=3 THEN now() END,CASE WHEN n=3 THEN @historic END,now(),now()
             FROM generate_series(1,3) n;
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,archived_at,deleted_at,deleted_by,created_at,updated_at)
             SELECT md5(@tenant::text||':list:'||n)::uuid,@tenant,md5(@tenant::text||':board:'||n)::uuid,'Page List','500000000000000000000000000000',
              CASE n WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,
              CASE WHEN n>=2 THEN now() END,CASE WHEN n=3 THEN now() END,CASE WHEN n=3 THEN @historic END,now(),now()
             FROM generate_series(1,3) n;
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,deleted_at,deleted_by,created_at,updated_at)
             SELECT md5(@tenant::text||':card:'||n)::uuid,@tenant,md5(@tenant::text||':board:'||(1+n%2))::uuid,
              md5(@tenant::text||':list:'||(1+n%2))::uuid,'Page Card','500000000000000000000000000000',
              CASE WHEN n=1 THEN 'DELETED' WHEN n%2=0 THEN 'ARCHIVED' ELSE 'ACTIVE' END,
              CASE WHEN n=1 OR n%2=0 THEN now() END,CASE WHEN n=1 THEN now() END,CASE WHEN n=1 THEN @historic END,now(),now()
             FROM generate_series(1,260) n;
            INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,mime_type,size_bytes,storage_key,sha256,scan_status,created_at,updated_at)
             SELECT md5(@tenant::text||':attachment:'||n)::uuid,@tenant,md5(@tenant::text||':card:'||(1+n%260))::uuid,@actor,
              CASE WHEN n=130 THEN 'FILE' ELSE 'URL' END,'Page source',CASE WHEN n<130 THEN 'https://example.test/retained' END,
              CASE WHEN n=130 THEN 'application/octet-stream' END,CASE WHEN n=130 THEN 3 END,
              CASE WHEN n=130 THEN 'private-fixture/'||@tenant::text||'/original.bin' END,CASE WHEN n=130 THEN repeat('0',64) END,
              CASE WHEN n=130 THEN 'PENDING' ELSE 'NOT_APPLICABLE' END,now(),now() FROM generate_series(1,130) n;
            UPDATE attachments SET lifecycle_state='ARCHIVED',archived_at=now(),updated_at=now(),version=2
             WHERE tenant_id=@tenant AND (id=md5(@tenant::text||':attachment:1')::uuid OR id IN
              (SELECT md5(@tenant::text||':attachment:'||n)::uuid FROM generate_series(2,130) n WHERE n%2=0));
            UPDATE attachments SET lifecycle_state='DELETED',deleted_at=now(),deleted_by=@historic,updated_at=now(),version=3
             WHERE tenant_id=@tenant AND id=md5(@tenant::text||':attachment:1')::uuid;
            CREATE TEMP TABLE deletion_page_archive_history AS
             SELECT 'Board'::text kind,id,archived_at FROM boards WHERE tenant_id=@tenant UNION ALL
             SELECT 'List',id,archived_at FROM board_lists WHERE tenant_id=@tenant UNION ALL
             SELECT 'Card',id,archived_at FROM cards WHERE tenant_id=@tenant UNION ALL
             SELECT 'Attachment',id,archived_at FROM attachments WHERE tenant_id=@tenant;
            CREATE TEMP TABLE deletion_page_deleted_history AS
             SELECT 'Board'::text kind,id,deleted_at,deleted_by FROM boards WHERE tenant_id=@tenant AND lifecycle_state='DELETED' UNION ALL
             SELECT 'List',id,deleted_at,deleted_by FROM board_lists WHERE tenant_id=@tenant AND lifecycle_state='DELETED' UNION ALL
             SELECT 'Card',id,deleted_at,deleted_by FROM cards WHERE tenant_id=@tenant AND lifecycle_state='DELETED' UNION ALL
             SELECT 'Attachment',id,deleted_at,deleted_by FROM attachments WHERE tenant_id=@tenant AND lifecycle_state='DELETED';
            """);
        await using var api=new PostgresConnectionFactory(apiConnection); await using var worker=new PostgresConnectionFactory(workerConnection);
        var job=OrganizationDeletionJobs.Create(tenant,actor,new(request,request,2),"page-original");
        await using(var publish=await api.OpenTenantSessionAsync(tenant,ct))
        {
            await using var command=new NpgsqlCommand("""
                INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id)
                 VALUES(@tenant,@request,@actor,2,'page-original');
                INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase) VALUES(@tenant,@request,@request,'ATTACHMENTS');
                """,publish.Connection,publish.Transaction);
            command.Parameters.AddWithValue("tenant",tenant); command.Parameters.AddWithValue("request",request); command.Parameters.AddWithValue("actor",actor);
            await command.ExecuteNonQueryAsync(ct); Require(await new PostgresBackgroundJobStore(api).PublishAsync(publish,job,ct),"Page fixture publication failed.");
            await publish.CommitAsync(ct);
        }
        var jobs=new PostgresBackgroundJobStore(worker); var store=new PostgresOrganizationDeletionPageStore(worker);
        var claim=await jobs.ClaimAsync(tenant,Guid.NewGuid(),ct)??throw new InvalidOperationException("Page fixture claim missing.");
        var attempt=new OrganizationDeletionAttempt(request,request,2);
        const string snapshot="""
            SELECT md5(jsonb_build_object(
             'attachments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE tenant_id=@tenant),
             'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id=@tenant),
             'progress',(SELECT to_jsonb(p) FROM organization_deletion_progress p WHERE tenant_id=@tenant),
             'steps',(SELECT jsonb_agg(to_jsonb(s) ORDER BY step_id) FROM organization_deletion_steps s WHERE tenant_id=@tenant),
             'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id=@tenant),
             'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id=@tenant),
             'queue',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id=@tenant AND job_type<>'ORGANIZATION_DELETE_PAGE'),
             'continuations',(SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ORGANIZATION_DELETE_PAGE'))::text);
            """;
        var initial=await Fingerprint(snapshot);
        foreach(var bad in new[]{claim with{OrganizationId=Guid.NewGuid()},claim with{LeaseId=Guid.NewGuid()},claim with{ActorId=Guid.NewGuid()}})
            Require(!await store.ApplyPageAsync(bad,attempt,128,ct),"Page authority fence failed.");
        Require(!await store.ApplyPageAsync(claim,attempt,129,ct),"Page bound bypassed.");
        await Admin($"""
            CREATE FUNCTION public.ci_deletion_page_expiry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.tenant_id='{tenant:D}'::uuid AND NEW.job_type='ORGANIZATION_DELETE_PAGE' AND NEW.id<>'{claim.Id:D}'::uuid THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;
             END IF; RETURN NEW; END $$;
            CREATE TRIGGER ci_deletion_page_expiry AFTER INSERT ON background_jobs FOR EACH ROW EXECUTE FUNCTION public.ci_deletion_page_expiry();
            """);
        try
        {
            var expired=false; try{await store.ApplyPageAsync(claim,attempt,128,ct);}
            catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.CheckViolation){expired=true;}
            Require(expired&&await Fingerprint(snapshot)==initial,"Late page expiry retained effects/history/progress/publication.");
        }
        finally{await Admin("DROP TRIGGER ci_deletion_page_expiry ON background_jobs; DROP FUNCTION public.ci_deletion_page_expiry();");}
        Require(await store.ApplyPageAsync(claim,attempt,128,ct),"Valid page failed.");
        var committed=await Fingerprint(snapshot);
        Require(committed!=initial&&await store.ApplyPageAsync(claim,attempt,128,ct)&&await Fingerprint(snapshot)==committed,
            "Committed page replay repeated effects or rewrote its checkpoint.");
        await Admin($"UPDATE background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;");
        Require(!await store.ApplyPageAsync(claim,attempt,128,ct),"Expired page replay succeeded.");
        var reclaimed=await jobs.ClaimAsync(tenant,Guid.NewGuid(),ct)??throw new InvalidOperationException("Page reclaim missing.");
        Require(reclaimed.Id==claim.Id&&await store.ApplyPageAsync(reclaimed,attempt,128,ct)&&await Fingerprint(snapshot)==committed,
            "Reclaimed page did not retain the original committed effects.");
        Require(await jobs.CompleteAsync(tenant,reclaimed.Id,reclaimed.LeaseId,reclaimed.WorkerId,ct),"Reclaimed page acknowledgment failed.");
        await Admin("UPDATE users SET status='DEACTIVATED',updated_at=now(),version=version+1 WHERE id=@actor;");
        var processor=new BackgroundJobProcessor(jobs,new SystemClock(),[
            new OrganizationDeletionPageHandler(store),new WorkEventDeliveryHandler(new PostgresWorkEventDeliveryStore(worker)),
            new OrganizationLifecycleDeliveryHandler(new PostgresOrganizationLifecycleDeliveryStore(worker))]);
        var completed=0;
        while(completed<1500)
        {
            var result=await processor.ProcessOneAsync(tenant,Guid.NewGuid(),ct); if(result==JobProcessingResult.Empty)break;
            Require(result==JobProcessingResult.Completed,"Deletion stage or event delivery did not complete."); completed++;
        }
        Require(completed>0&&completed<1500,"Deletion graph did not drain bounded continuations.");
        await using(var verify=new NpgsqlCommand("""
            SELECT o.status,p.phase,
             (SELECT count(*) FROM organization_deletion_steps WHERE tenant_id=@tenant AND candidate_count>128),
             (SELECT count(*) FROM boards WHERE tenant_id=@tenant AND lifecycle_state<>'DELETED')+
              (SELECT count(*) FROM board_lists WHERE tenant_id=@tenant AND lifecycle_state<>'DELETED')+
              (SELECT count(*) FROM cards WHERE tenant_id=@tenant AND lifecycle_state<>'DELETED')+
              (SELECT count(*) FROM attachments WHERE tenant_id=@tenant AND lifecycle_state<>'DELETED'),
             (SELECT count(*) FROM organization_lifecycle_events WHERE tenant_id=@tenant AND ready_at IS NOT NULL),
             (SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND state<>'SUCCEEDED'),
             (SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND actor_id<>@actor),
             (SELECT storage_key FROM attachments WHERE tenant_id=@tenant AND kind='FILE'),
             (SELECT sha256 FROM attachments WHERE tenant_id=@tenant AND kind='FILE')
            FROM organizations o JOIN organization_deletion_progress p ON p.tenant_id=o.id WHERE o.id=@tenant;
            """,admin))
        {
            verify.Parameters.AddWithValue("tenant",tenant);verify.Parameters.AddWithValue("actor",actor);
            await using var rows=await verify.ExecuteReaderAsync(ct);
            Require(await rows.ReadAsync(ct)&&rows.GetString(0)=="DELETED"&&rows.GetString(1)=="COMPLETE"
                &&rows.GetInt64(2)==0&&rows.GetInt64(3)==0&&rows.GetInt64(4)==1&&rows.GetInt64(5)==0&&rows.GetInt64(6)==0
                &&rows.GetString(7)==$"private-fixture/{tenant:D}/original.bin"&&rows.GetString(8)==new string('0',64),
                "Bounded graph completion, retained actor/provider evidence or event delivery failed.");
        }
        var preserved=await Fingerprint("""
            SELECT (NOT EXISTS(SELECT 1 FROM deletion_page_archive_history h JOIN (
             SELECT 'Board' kind,id,archived_at FROM boards WHERE tenant_id=@tenant UNION ALL
             SELECT 'List',id,archived_at FROM board_lists WHERE tenant_id=@tenant UNION ALL
             SELECT 'Card',id,archived_at FROM cards WHERE tenant_id=@tenant UNION ALL
             SELECT 'Attachment',id,archived_at FROM attachments WHERE tenant_id=@tenant) c USING(kind,id)
             WHERE h.archived_at IS DISTINCT FROM c.archived_at)
            AND NOT EXISTS(SELECT 1 FROM deletion_page_deleted_history h JOIN (
             SELECT 'Board' kind,id,deleted_at,deleted_by FROM boards WHERE tenant_id=@tenant UNION ALL
             SELECT 'List',id,deleted_at,deleted_by FROM board_lists WHERE tenant_id=@tenant UNION ALL
             SELECT 'Card',id,deleted_at,deleted_by FROM cards WHERE tenant_id=@tenant UNION ALL
             SELECT 'Attachment',id,deleted_at,deleted_by FROM attachments WHERE tenant_id=@tenant) c USING(kind,id)
             WHERE h.deleted_at IS DISTINCT FROM c.deleted_at OR h.deleted_by IS DISTINCT FROM c.deleted_by))::text;
            """);
        Require(preserved=="true","Deletion stages rewrote prior archive/deletion attribution.");
        await Admin("DROP TABLE deletion_page_archive_history,deletion_page_deleted_history;");
        Console.WriteLine("Organization deletion pages: all graph stages, 128-candidate bounds, late rollback, duplicate/reclaim recovery, retained prior history/provider metadata and independent actor completion passed.");
    }
}
