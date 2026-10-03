using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

internal static class AttachmentScanRecoveryContract
{
    internal static async Task RunAsync(NpgsqlConnection admin,PostgresConnectionFactory worker,PostgresConnectionFactory api,
        Guid tenant,Guid card,Func<string,Task<Guid>> publish,Func<ClaimedBackgroundJob,Task> completeClean,
        Func<int> providerReads,CancellationToken ct)
    {
        void Require(bool condition,string invariant){if(!condition)throw new InvalidOperationException(invariant);}
        var store=new PostgresAttachmentScanRecoveryStore(worker);
        var queue=new PostgresBackgroundJobStore(worker);var workerId=Guid.NewGuid();
        async Task<T> Scalar<T>(string sql,Guid job=default,Guid file=default)
        {
            await using var query=new NpgsqlCommand(sql,admin);
            query.Parameters.AddWithValue("tenant",tenant);query.Parameters.AddWithValue("card",card);
            query.Parameters.AddWithValue("job",job);query.Parameters.AddWithValue("file",file);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        async Task<(Guid File,ClaimedBackgroundJob Job)> Pending()
        {
            var file=await publish("image/png");var claim=await queue.ClaimAsync(tenant,workerId,ct);
            Require(claim?.JobType==AttachmentScanJobs.Type && AttachmentScanAttempt.Parse(claim.SafeMetadataJson).AttachmentId==file,
                "Scan recovery fixture did not claim its canonical quarantine source.");
            return(file,claim!);
        }
        foreach(var factory in new[]{api,worker})
        {
            await using var session=await factory.OpenTenantSessionAsync(tenant,ct);
            await using var denied=new NpgsqlCommand("SELECT count(*) FROM public.attachment_scan_sweeps;",session.Connection,session.Transaction);
            try{await denied.ExecuteScalarAsync(ct);throw new InvalidOperationException("Runtime read the private scan recovery cursor.");}
            catch(PostgresException error)when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege){}
        }
        await using(var session=await api.OpenTenantSessionAsync(tenant,ct))
        await using(var denied=new NpgsqlCommand("SELECT * FROM public.recover_attachment_scan_page(@tenant,32);",session.Connection,session.Transaction))
        {
            denied.Parameters.AddWithValue("tenant",tenant);
            try{await denied.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("API acquired terminal scan recovery.");}
            catch(PostgresException error)when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege){}
        }
        await using(var session=await worker.OpenTenantSessionAsync(tenant,ct))
        await using(var wrong=new NpgsqlCommand("SELECT visited=0 AND recovered=0 FROM public.recover_attachment_scan_page(@foreign,32);",session.Connection,session.Transaction))
        {
            wrong.Parameters.AddWithValue("foreign",Guid.NewGuid());
            Require(await wrong.ExecuteScalarAsync(ct) is true,"Scan recovery crossed tenant context.");
            await session.CommitAsync(ct);
        }
        foreach(var limit in new[]{0,33})
        {
            try{await store.RecoverPageAsync(tenant,limit,ct);throw new InvalidOperationException("Unbounded scan recovery was accepted.");}
            catch(ArgumentOutOfRangeException){}
        }
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try{await store.RecoverPageAsync(tenant,32,cancelled.Token);throw new InvalidOperationException("Cancelled recovery opened DB work.");}
            catch(OperationCanceledException){}
        }
        // More than one page of ineligible live work must not starve later
        // exhausted jobs. All are real publication/claim transactions.
        for(var index=0;index<36;index++)
        {
            var live=await Pending();
            await Scalar<int>("UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()+interval '1 day',version=version+1 WHERE id=@job AND tenant_id=@tenant RETURNING 1;",live.Job.Id);
        }
        var expired=await Pending();var queuedExpiry=await Pending();var liveFinal=await Pending();var nonFinal=await Pending();
        var removed=await Pending();var stale=await Pending();var fence=await Pending();var clean=await Pending();
        await completeClean(clean.Job);
        await Scalar<int>("UPDATE public.background_jobs SET attempt_count=max_attempts,lease_expires_at=clock_timestamp()-interval '1 second',version=version+1 WHERE id=@job AND tenant_id=@tenant RETURNING 1;",queuedExpiry.Job.Id);
        Require(await queue.ClaimAsync(tenant,workerId,ct) is null,"Queue expiry unexpectedly claimed live work.");
        Require(await Scalar<bool>("SELECT state='FAILED' AND last_error_code='lease_expired' FROM public.background_jobs WHERE id=@job;",queuedExpiry.Job.Id),
            "Normal queue did not establish exhausted terminal expiry.");
        foreach(var entry in new[]{expired,removed,stale,clean})
            await Scalar<int>("UPDATE public.background_jobs SET attempt_count=max_attempts,lease_expires_at=clock_timestamp()-interval '1 second',version=version+1 WHERE id=@job AND tenant_id=@tenant RETURNING 1;",entry.Job.Id);
        await Scalar<int>("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;",file:expired.File);
        await Scalar<int>("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant; UPDATE public.attachments SET lifecycle_state='ACTIVE',version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;",file:queuedExpiry.File);
        await Scalar<int>("UPDATE public.background_jobs SET attempt_count=max_attempts,lease_expires_at=clock_timestamp()+interval '1 day',version=version+1 WHERE id=@job AND tenant_id=@tenant RETURNING 1;",liveFinal.Job.Id);
        await Scalar<int>("UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second',version=version+1 WHERE id=@job AND tenant_id=@tenant RETURNING 1;",nonFinal.Job.Id);
        await Scalar<int>("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant; UPDATE public.attachments SET lifecycle_state='DELETED',deleted_by=uploader_id,deleted_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;",file:removed.File);
        await Scalar<int>("UPDATE public.attachments SET version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;",file:stale.File);
        // Move after the source jobs were claimed. Recovery must publish on the
        // current stable Card's Board, preserving the old stream sequence.
        var oldBoard=await Scalar<Guid>("SELECT board_id FROM public.cards WHERE id=@card AND tenant_id=@tenant;");
        var oldSequence=await Scalar<long>("SELECT COALESCE((SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card)),0);");
        var movedBoard=Guid.NewGuid();var movedList=Guid.NewGuid();
        await using(var move=new NpgsqlCommand("""
            INSERT INTO public.boards(id,tenant_id,name,created_at,updated_at)
             VALUES(@board,@tenant,'Recovered scan moved Board',clock_timestamp(),clock_timestamp());
            INSERT INTO public.board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
             VALUES(@list,@tenant,@board,'Recovered scan moved List','500000000000000000000000000000',clock_timestamp(),clock_timestamp());
            UPDATE public.cards SET board_id=@board,list_id=@list,updated_at=GREATEST(updated_at,clock_timestamp()),version=version+1 WHERE id=@card AND tenant_id=@tenant;
            """,admin))
        {
            move.Parameters.AddWithValue("board",movedBoard);move.Parameters.AddWithValue("list",movedList);
            move.Parameters.AddWithValue("tenant",tenant);move.Parameters.AddWithValue("card",card);await move.ExecuteNonQueryAsync(ct);
        }
        var cardVersion=await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;");
        var beforeReads=providerReads();
        var first=await store.RecoverPageAsync(tenant,32,ct);
        Require(first.Visited==32 && first.Recovered==0,"Recovery did not bound its page before eligibility checks.");
        await using(var held=await admin.BeginTransactionAsync(ct))
        {
            await using var gate=new NpgsqlCommand("SELECT tenant_id FROM public.attachment_scan_sweeps WHERE tenant_id=@tenant FOR UPDATE;",admin,held);
            gate.Parameters.AddWithValue("tenant",tenant);await gate.ExecuteScalarAsync(ct);
            Require(await store.RecoverPageAsync(tenant,32,ct)==new AttachmentScanRecoveryResult(0,0),"Replica advanced an owned scan recovery cursor.");
            await held.RollbackAsync(ct);
        }
        for(var pass=0;pass<8;pass++)await store.RecoverPageAsync(tenant,32,ct);
        foreach(var entry in new[]{expired,queuedExpiry})
        {
            Require(await Scalar<bool>("SELECT scan_status='FAILED' AND scanned_at IS NOT NULL AND archived_at IS NOT NULL AND "
                +(entry.File==expired.File?"version=3 AND lifecycle_revision=1 AND lifecycle_state='ARCHIVED'":"version=4 AND lifecycle_revision=2 AND lifecycle_state='ACTIVE'")
                +" FROM public.attachments WHERE id=@file AND tenant_id=@tenant;",file:entry.File),
                "Exhausted quarantine remained Pending.");
            Require(await Scalar<bool>("SELECT state='FAILED' AND attempt_count=max_attempts AND lease_id IS NULL AND worker_id IS NULL AND lease_expires_at IS NULL FROM public.background_jobs WHERE id=@job;",entry.Job.Id),
                "Recovery did not retain terminal job state.");
            Require(await Scalar<long>("SELECT count(*) FROM public.audit_events a JOIN public.work_events e ON e.event_id=a.id AND e.tenant_id=a.tenant_id JOIN public.cards c ON c.id=e.entity_id AND c.tenant_id=e.tenant_id WHERE a.id=@job AND a.tenant_id=@tenant AND a.event_type='ATTACHMENT_SCAN_COMPLETED' AND a.entity_type='Attachment' AND a.entity_id=@file AND e.event_type=a.event_type AND e.board_id=c.board_id AND e.ready_at=e.created_at AND e.actor_id=a.actor_id;",entry.Job.Id,entry.File)==1,
                "Recovery lost its atomic current-Board audit/ready event.");
            Require(!await queue.CompleteAsync(tenant,entry.Job.Id,entry.Job.LeaseId,workerId,ct),"An old expired claim acknowledged recovered work.");
        }
        Require(await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;")==cardVersion+2,
            "Recovery duplicated Card revisions on replay.");
        await using(var oldStream=new NpgsqlCommand("SELECT COALESCE((SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=@board),0);",admin))
        {
            oldStream.Parameters.AddWithValue("tenant",tenant);oldStream.Parameters.AddWithValue("board",oldBoard);
            Require((long)(await oldStream.ExecuteScalarAsync(ct))! ==oldSequence,"Moved scan recovery advanced the old Board.");
        }
        foreach(var entry in new[]{liveFinal,nonFinal,stale})
            Require(await Scalar<bool>("SELECT scan_status='PENDING' AND scanned_at IS NULL FROM public.attachments WHERE id=@file AND tenant_id=@tenant;",file:entry.File),
                "Live, non-final or stale claim converted quarantine into failure.");
        Require(await Scalar<bool>("SELECT scan_status='CLEAN' AND version=2 FROM public.attachments WHERE id=@file AND tenant_id=@tenant;",file:clean.File),
            "Recovery overwrote a committed Clean verdict after acknowledgement loss.");
        Require(await Scalar<long>("SELECT count(*) FROM public.work_events WHERE tenant_id=@tenant AND event_id=@job;",removed.Job.Id)==0,
            "Recovery published a deleted source.");
        Require(providerReads()==beforeReads,"Terminal metadata recovery read private provider bytes.");

        // Lose terminal evidence AFTER tentative File/Card/audit/event effects.
        // The last fence must roll back everything, including its sweep cursor.
        await Scalar<int>("UPDATE public.background_jobs SET attempt_count=max_attempts,lease_expires_at=clock_timestamp()-interval '1 second',version=version+1 WHERE id=@job AND tenant_id=@tenant RETURNING 1;",fence.Job.Id);
        cardVersion=await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;");
        var sequence=await Scalar<long>("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card);");
        var jobSnapshot=await Scalar<string>("SELECT to_jsonb(j)::text FROM public.background_jobs j WHERE id=@job;",fence.Job.Id);
        var cursor=await Scalar<string>("SELECT to_jsonb(s)::text FROM public.attachment_scan_sweeps s WHERE tenant_id=@tenant;");
        var trigger="ci_scan_recovery_"+fence.Job.Id.ToString("N");
        await using(var install=new NpgsqlCommand($"""
            CREATE FUNCTION public.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NEW.event_id='{fence.Job.Id:D}'::uuid THEN
              UPDATE public.background_jobs SET state='RUNNING',lease_id=gen_random_uuid(),worker_id=gen_random_uuid(),
               lease_expires_at=clock_timestamp()+interval '1 day',version=version+1 WHERE id=NEW.event_id AND tenant_id=NEW.tenant_id;
             END IF; RETURN NEW;
            END $$;
            CREATE TRIGGER {trigger} AFTER INSERT ON public.work_events FOR EACH ROW EXECUTE FUNCTION public.{trigger}();
            """,admin))await install.ExecuteNonQueryAsync(ct);
        try
        {
            var refused=false;
            // The fixed cursor may need one page of live jobs before the target.
            for(var pass=0;pass<3 && !refused;pass++)
            {
                cursor=await Scalar<string>("SELECT to_jsonb(s)::text FROM public.attachment_scan_sweeps s WHERE tenant_id=@tenant;");
                try{await store.RecoverPageAsync(tenant,32,ct);}
                catch(PostgresException error)when(error.SqlState==PostgresErrorCodes.CheckViolation && error.MessageText=="Attachment scan recovery terminal fence failed"){refused=true;}
            }
            Require(refused,"Changed terminal evidence did not refuse recovery publication.");
            Require(await Scalar<string>("SELECT to_jsonb(s)::text FROM public.attachment_scan_sweeps s WHERE tenant_id=@tenant;")==cursor,
                "Failed recovery advanced its cursor.");
            Require(await Scalar<string>("SELECT to_jsonb(j)::text FROM public.background_jobs j WHERE id=@job;",fence.Job.Id)==jobSnapshot,
                "Failed recovery retained job/lease changes.");
            Require(await Scalar<bool>("SELECT scan_status='PENDING' AND version=1 FROM public.attachments WHERE id=@file;",file:fence.File),
                "Failed recovery retained the File verdict.");
            Require(await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;")==cardVersion
                && await Scalar<long>("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card);")==sequence,
                "Failed recovery retained Card or stream effects.");
            Require(await Scalar<long>("SELECT count(*) FROM public.work_events WHERE event_id=@job AND tenant_id=@tenant;",fence.Job.Id)==0
                && await Scalar<long>("SELECT count(*) FROM public.audit_events WHERE id=@job AND tenant_id=@tenant;",fence.Job.Id)==0,
                "Failed recovery retained audit/event evidence.");
        }
        finally
        {
            await using var remove=new NpgsqlCommand($"DROP TRIGGER {trigger} ON public.work_events; DROP FUNCTION public.{trigger}();",admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        for(var pass=0;pass<3;pass++)await store.RecoverPageAsync(tenant,32,ct);
        Require(await Scalar<bool>("SELECT scan_status='FAILED' AND version=2 FROM public.attachments WHERE id=@file;",file:fence.File)
            && providerReads()==beforeReads,"Refused terminal recovery could not safely resume without provider I/O.");
        Console.WriteLine("Restricted scan recovery: Worker-only tenant scope, bounded cursor/replica locks, exhausted Failed and expired final claims, live/non-final/stale/deleted refusal, retained Clean verdict, moved-Board atomic effects, replay, late terminal fence rollback and no provider I/O passed.");
    }
}
