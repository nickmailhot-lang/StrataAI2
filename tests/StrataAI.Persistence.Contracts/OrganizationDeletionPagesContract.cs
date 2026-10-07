using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Application.Identity;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Onboarding;
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
            -- Admin-seeded historical publication with all normal constraints and
            -- triggers enabled. This is metadata evidence, not a provider write.
            CREATE TEMP TABLE deletion_page_image_fixture AS SELECT @tenant AS tenant,@actor AS actor,
             md5(@tenant::text||':image-card')::uuid AS card,md5(@tenant::text||':image-file')::uuid AS file,
             md5(@tenant::text||':image-preview')::uuid AS preview,md5(@tenant::text||':board:1')::uuid AS board,
             now() AS published;
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
             SELECT card,tenant,board,md5(tenant::text||':list:1')::uuid,'Cover fixture','600000000000000000000000000000',published,published
             FROM deletion_page_image_fixture;
            INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,mime_type,size_bytes,storage_key,sha256,
             scan_status,scanned_at,version,created_at,updated_at)
             SELECT file,tenant,card,actor,'FILE','Retained image','image/png',128,'private-fixture/'||tenant::text||'/image.png',
              repeat('a',64),'CLEAN',published,3,published,published FROM deletion_page_image_fixture;
            INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,state)
             SELECT preview,tenant,'ATTACHMENT_PREVIEW','attachment-preview/'||replace(file::text,'-','')||'/2',actor,
              'attachment-private-preview','page-preview',jsonb_build_object('attachmentId',file,'cardId',card,'version',2),'SUCCEEDED'
             FROM deletion_page_image_fixture;
            INSERT INTO attachment_previews(id,tenant_id,attachment_id,card_id,source_version,source_size_bytes,source_sha256,
             source_mime_type,output_size_bytes,output_sha256,width,height,created_at)
             SELECT preview,tenant,file,card,2,128,repeat('a',64),'image/png',64,repeat('b',64),1,1,published
             FROM deletion_page_image_fixture;
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
             SELECT preview,tenant,actor,'ATTACHMENT_PREVIEW_PUBLISHED','Attachment',file,'page-preview',published FROM deletion_page_image_fixture;
            INSERT INTO work_event_streams(tenant_id,board_id,last_sequence,updated_at)
             SELECT tenant,board,1,published FROM deletion_page_image_fixture;
            INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,
             correlation_id,created_at,ready_at)
             SELECT tenant,preview,board,1,actor,'ATTACHMENT_PREVIEW_PUBLISHED','Card',card,1,'page-preview',published,published
             FROM deletion_page_image_fixture;
            INSERT INTO attachment_preview_publications(id,tenant_id,attachment_version,card_version,board_id,published_at)
             SELECT preview,tenant,3,1,board,published FROM deletion_page_image_fixture;
            INSERT INTO board_background_images(id,tenant_id,board_id,preview_id,created_by,created_at)
             SELECT md5(tenant::text||':image-owner:1')::uuid,tenant,board,preview,actor,published FROM deletion_page_image_fixture;
            INSERT INTO board_background_images(id,tenant_id,board_id,preview_id,created_by,created_at,source_image_id)
             SELECT md5(tenant::text||':image-owner:'||n)::uuid,tenant,md5(tenant::text||':board:'||n)::uuid,preview,actor,published,
              md5(tenant::text||':image-owner:1')::uuid FROM deletion_page_image_fixture CROSS JOIN generate_series(2,3) n;
            UPDATE boards SET background_type='IMAGE',background_value=md5(tenant_id::text||':image-owner:'||n)::uuid::text,
             version=version+1,updated_at=GREATEST(updated_at,now())
             FROM generate_series(1,3) n WHERE tenant_id=@tenant AND id=md5(@tenant::text||':board:'||n)::uuid;
            UPDATE cards SET cover_attachment_id=f.file,version=version+1,updated_at=GREATEST(updated_at,f.published)
             FROM deletion_page_image_fixture f WHERE id=f.card AND tenant_id=f.tenant;
            CREATE TEMP TABLE deletion_page_provider_history AS
             SELECT 'Manifest'::text kind,id,to_jsonb(m) evidence FROM attachment_previews m WHERE tenant_id=@tenant UNION ALL
             SELECT 'Publication',id,to_jsonb(p) FROM attachment_preview_publications p WHERE tenant_id=@tenant UNION ALL
             SELECT 'ImageOwner',id,to_jsonb(i) FROM board_background_images i WHERE tenant_id=@tenant;
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
        // Only the public page capability is granted. Its trigger/event helpers
        // must remain private even when runtime roles are provisioned again.
        foreach(var runtime in new[]{api,worker})
        {
            await using var scope=await runtime.OpenTenantSessionAsync(tenant,ct);
            await using var permissions=new NpgsqlCommand("""
                SELECT NOT has_function_privilege(current_user,
                  'public.organization_deletion_page_is_live(uuid,uuid)','EXECUTE')
                 AND NOT has_function_privilege(current_user,
                  'public.append_organization_deletion_work_event(uuid,uuid,uuid,text,text,uuid,bigint,text,timestamptz)','EXECUTE');
                """,scope.Connection,scope.Transaction);
            Require(await permissions.ExecuteScalarAsync(ct) is true,"Private deletion helpers granted to a runtime caller.");
        }
        var job=OrganizationDeletionJobs.Create(tenant,actor,new(request,request,2),"page-original");
        await using(var publish=await api.OpenTenantSessionAsync(tenant,ct))
        {
            await using var command=new NpgsqlCommand("""
                INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id)
                 VALUES(@tenant,@request,@actor,2,'page-original');
                INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase) VALUES(@tenant,@request,@request,'ATTACHMENTS');
                INSERT INTO organization_deletion_replays(tenant_id,actor_id,key_id,fingerprint,expires_at)
                 VALUES(@tenant,@actor,@request,@fingerprint,clock_timestamp()+interval '24 hours');
                """,publish.Connection,publish.Transaction);
            command.Parameters.AddWithValue("tenant",tenant); command.Parameters.AddWithValue("request",request); command.Parameters.AddWithValue("actor",actor);
            command.Parameters.AddWithValue("fingerprint",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{organizationId=tenant,expectedVersion=1L}))));
            await command.ExecuteNonQueryAsync(ct); Require(await new PostgresBackgroundJobStore(api).PublishAsync(publish,job,ct),"Page fixture publication failed.");
            await publish.CommitAsync(ct);
        }
        // Real restricted metadata/read boundaries with a synthetic account
        // fence. Actual HTTP cookie/session behavior is covered by API-host tests.
        var admission=new ObservationAccountFixture(api,tenant);
        var observation=new PostgresOrganizationDeletionObservationReader(api,admission,
            NullLogger<PostgresOrganizationDeletionObservationReader>.Instance);
        var pending=await observation.ReadAsync(tenant,actor,request,ct);
        Require(pending.Succeeded&&pending.Value is {State:"PENDING",Version:2,EventId:null,CompletedAt:null},
            "Accepted deletion request could not be observed independently of ordinary parent admission.");
        Require((await observation.ReadAsync(tenant,actor,Guid.NewGuid(),ct)).ErrorCode=="organization_not_found"
            &&(await observation.ReadAsync(Guid.NewGuid(),actor,request,ct)).ErrorCode=="organization_not_found"
            &&(await observation.ReadAsync(tenant,historic,request,ct)).ErrorCode=="organization_not_found",
            "Deletion observation admitted a foreign request, scope or requester.");
        admission.RefuseFinal=true;
        Require((await observation.ReadAsync(tenant,actor,request,ct)).ErrorCode=="session_unavailable",
            "Deletion observation disclosed pending status after final admission failure.");
        admission.RefuseFinal=false;
        var jobs=new PostgresBackgroundJobStore(worker); var store=new PostgresOrganizationDeletionPageStore(worker);
        var claim=await jobs.ClaimAsync(tenant,Guid.NewGuid(),ct)??throw new InvalidOperationException("Page fixture claim missing.");
        var attempt=new OrganizationDeletionAttempt(request,request,2);
        const string snapshot="""
            SELECT md5(jsonb_build_object(
             'attachments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE tenant_id=@tenant),
             'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id=@tenant),
             'boards',(SELECT jsonb_agg(to_jsonb(b) ORDER BY id) FROM boards b WHERE tenant_id=@tenant),
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
            new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker)),
            new OrganizationLifecycleDeliveryHandler(new PostgresOrganizationLifecycleDeliveryStore(worker))]);
        var completed=0;var terminalDiscovered=false;
        while(completed<1500)
        {
            var result=await processor.ProcessOneAsync(tenant,Guid.NewGuid(),ct); if(result==JobProcessingResult.Empty)break;
            Require(result==JobProcessingResult.Completed,"Deletion stage or event delivery did not complete."); completed++;
            if(!terminalDiscovered)
            {
                await using var pendingDelivery=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id=@tenant AND job_type='ORGANIZATION_LIFECYCLE_EVENT_READY' AND state='PENDING');",admin);
                pendingDelivery.Parameters.AddWithValue("tenant",tenant);
                if(await pendingDelivery.ExecuteScalarAsync(ct) is true)
                {
                    Require((await new PostgresOrganizationDeletionScopeReader(worker).ReadAsync(null,100,ct)).Contains(tenant),
                        "Terminal parent disappeared from discovery before leased completion delivery.");
                    terminalDiscovered=true;
                }
            }
        }
        Require(completed>0&&completed<1500,"Deletion graph did not drain bounded continuations.");
        Require(terminalDiscovered,"Deletion discovery did not cover terminal-event routing.");
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
             (SELECT storage_key FROM attachments WHERE tenant_id=@tenant AND id=md5(@tenant::text||':attachment:130')::uuid),
             (SELECT sha256 FROM attachments WHERE tenant_id=@tenant AND id=md5(@tenant::text||':attachment:130')::uuid)
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
        Require(await Fingerprint("""
            SELECT (NOT EXISTS(SELECT 1 FROM cards WHERE tenant_id=@tenant AND cover_attachment_id IS NOT NULL)
             AND NOT EXISTS(SELECT 1 FROM boards WHERE tenant_id=@tenant AND
              (background_image_id IS NOT NULL OR background_type<>'COLOR' OR background_value IS NOT NULL))
             AND (SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND event_type='CARD_COVER_CHANGED')=1
             AND (SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND event_type='BOARD_UPDATED')=1
             AND NOT EXISTS(SELECT 1 FROM deletion_page_provider_history h FULL JOIN (
              SELECT 'Manifest' kind,id,to_jsonb(m) evidence FROM attachment_previews m WHERE tenant_id=@tenant UNION ALL
              SELECT 'Publication',id,to_jsonb(p) FROM attachment_preview_publications p WHERE tenant_id=@tenant UNION ALL
              SELECT 'ImageOwner',id,to_jsonb(i) FROM board_background_images i WHERE tenant_id=@tenant) c USING(kind,id)
              WHERE h.evidence IS DISTINCT FROM c.evidence)
             AND EXISTS(SELECT 1 FROM attachments a JOIN deletion_page_image_fixture f ON a.id=f.file
              WHERE a.lifecycle_state='DELETED' AND a.archived_at IS NULL AND a.scan_status='CLEAN'
               AND a.storage_key='private-fixture/'||@tenant::text||'/image.png' AND a.sha256=repeat('a',64)))::text;
            """)=="true","Selected cover/background cleanup or immutable preview ownership retention failed.");
        Require((await observation.ReadAsync(tenant,actor,request,ct)).ErrorCode=="session_unavailable",
            "Deactivated deleting actor observed completion.");
        // Re-enable only the disposable fixture account after proving the Worker
        // finished without it, to test current Owner observation of terminal state.
        await Admin("UPDATE users SET status='ACTIVE',updated_at=now(),version=version+1 WHERE id=@actor;");
        var terminal=await observation.ReadAsync(tenant,actor,request,ct);
        Require(terminal.Succeeded&&terminal.Value is {State:"COMPLETED",Version:3,EventId:not null,CompletedAt:not null}
            &&terminal.Value.RequestId==request,"Authoritative terminal completion observation failed.");
        var again=await observation.ReadAsync(tenant,actor,request,ct);
        Require(again.Value==terminal.Value,"Completion recovery rewrote the original event/time/version.");
        await Admin("UPDATE organization_members SET role='ADMIN',version=version+1 WHERE tenant_id=@tenant AND user_id=@actor;");
        Require((await observation.ReadAsync(tenant,actor,request,ct)).ErrorCode=="organization_not_found",
            "Former Owner observed terminal completion after demotion.");
        await Admin("UPDATE organization_members SET role='OWNER',version=version+1 WHERE tenant_id=@tenant AND user_id=@actor;");
        admission.RefuseFinal=true;
        Require((await observation.ReadAsync(tenant,actor,request,ct)).ErrorCode=="session_unavailable",
            "Deletion observation disclosed completion after final admission failure.");
        admission.RefuseFinal=false;
        var recoveryServices=new ServiceCollection();recoveryServices.AddLogging();
        recoveryServices.AddSingleton(api);recoveryServices.AddSingleton<ICommandActorAuthorization>(admission);
        recoveryServices.AddStrataAiOrganizations(new(RuntimeMode.Production,"contract","contract"));
        await using var recoveryProvider=recoveryServices.BuildServiceProvider();
        var recoveryUnit=recoveryProvider.GetRequiredService<IOrganizationUnitOfWork>();
        var receipts=recoveryProvider.GetRequiredService<IOrganizationDeletionReplayStore>();
        var normalCallback=false;
        var ordinary=await recoveryUnit.ExecuteAsync(tenant,actor,null,false,()=>{
            normalCallback=true;return Task.FromResult(OrganizationOperation<bool>.Success(true));
        },ct);
        Require(!ordinary.Succeeded&&!normalCallback,"Normal command admitted a terminal Organization.");
        async Task<OrganizationOperation<bool>> Recover()=>await recoveryUnit.ExecuteAsync(tenant,actor,null,false,async()=>{
            var receipt=await receipts.ReadAsync(tenant,actor,request,ct);
            Require(receipt is not null,"Terminal recovery lost the original request acknowledgment.");
            return OrganizationOperation<bool>.Success(true);
        },ct,allowDeletionRecovery:true);
        Require((await Recover()).Succeeded,"Retained acknowledgment could not be read after terminal completion.");
        admission.RefuseFinal=true;
        Require((await Recover()).ErrorCode=="session_unavailable","Terminal acknowledgment survived final actor refusal.");
        admission.RefuseFinal=false;
        Require((await observation.ReadAsync(tenant,actor,request,ct)).Value==terminal.Value,"Acknowledgment recovery changed completion evidence.");
        Console.WriteLine("Organization deletion acknowledgment: terminal recovery scope retains receipt, normal commands remain withdrawn, final actor refusal preserves completion passed.");
        Console.WriteLine("Organization deletion observation: restricted pending/terminal reads, foreign requester/scope denial, current Owner/account admission, final fence and stable completion recovery passed.");
        await Admin("DROP TABLE deletion_page_archive_history,deletion_page_deleted_history,deletion_page_provider_history,deletion_page_image_fixture;");
        Console.WriteLine("Organization deletion pages: all graph stages, 128-candidate bounds, late rollback, duplicate/reclaim recovery, selected cover/background cleanup, retained prior history/provider metadata and independent actor completion passed.");
    }
    private sealed class ObservationAccountFixture(PostgresConnectionFactory connections,Guid tenant):ICommandActorAuthorization
    {
        private int _calls;
        private bool _refuseFinal;
        public bool RefuseFinal {get=>_refuseFinal;set{_refuseFinal=value;_calls=0;}}
        public async Task<bool> VerifyAsync(Guid actorId,CancellationToken cancellationToken=default)
        {
            if(_refuseFinal&&++_calls==2)return false;
            await using var scope=await connections.OpenTenantSessionAsync(tenant,cancellationToken);
            await using var query=new NpgsqlCommand("SELECT status='ACTIVE' FROM users WHERE id=@actor FOR SHARE;",scope.Connection,scope.Transaction);
            query.Parameters.AddWithValue("actor",actorId);
            return await query.ExecuteScalarAsync(cancellationToken) is true;
        }
    }
}
