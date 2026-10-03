using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

internal static class AttachmentWorkerContract
{
    internal static async Task RunAsync(NpgsqlConnection admin,string workerConnection,ServiceProvider apiServices,
        Guid organization,Guid foreignOrganization,AttachmentUploadIntent original,byte[] bytes,string digest,CancellationToken ct)
    {
        void Require(bool condition,string invariant) {if(!condition)throw new InvalidOperationException(invariant);}
        var api=apiServices.GetRequiredService<PostgresConnectionFactory>();
        await using var worker=new PostgresConnectionFactory(workerConnection); var queue=new PostgresBackgroundJobStore(worker,previewJobs:true);
        var delivery=new PostgresAttachmentScanDeliveryStore(worker); var workerId=Guid.NewGuid();
        var storage=new FixtureStorage(organization,bytes); var scanner=new FixtureScanner();
        var handler=new AttachmentScanDeliveryHandler(delivery,new(storage,scanner));
        var job=await queue.ClaimAsync(organization,workerId,ct); Require(job is not null && job.JobType==AttachmentScanJobs.Type,"Worker did not claim scan job.");
        var claim=job!; var attempt=AttachmentScanAttempt.Parse(claim.SafeMetadataJson);
        Require(attempt.AttachmentId==original.Id && attempt.CardId==original.CardId,"Worker claimed wrong canonical file.");
        var loaded=await delivery.LoadAsync(claim,attempt,ct);
        Require(loaded.Status==AttachmentScanLoadStatus.Ready && loaded.Request?.Sha256==digest && loaded.Request.SizeBytes==bytes.Length,"Worker integrity admission failed.");
        foreach(var altered in new[] {claim with {OrganizationId=foreignOrganization},claim with {LeaseId=Guid.NewGuid()},claim with {WorkerId=Guid.NewGuid()},claim with {ActorId=Guid.NewGuid()}})
        {
            var denied=await delivery.LoadAsync(altered,attempt,ct);
            Require(denied.Status==AttachmentScanLoadStatus.LeaseLost && denied.Request is null,"Unproven Worker claim disclosed integrity.");
        }
        var wrongReference=claim with {SafeMetadataJson=JsonSerializer.Serialize(new {attachmentId=original.Id,cardId=Guid.NewGuid(),version=1})};
        Require((await delivery.LoadAsync(wrongReference,AttachmentScanAttempt.Parse(wrongReference.SafeMetadataJson),ct)) is {Status:AttachmentScanLoadStatus.LeaseLost,Request:null},"Forged Card reference disclosed integrity.");
        foreach(var payload in new[] {"[]","null","{}",
            JsonSerializer.Serialize(new {attachmentId=original.Id,cardId=original.CardId,version=0}),
            JsonSerializer.Serialize(new {attachmentId=original.Id,cardId=original.CardId,version=long.MaxValue}),
            JsonSerializer.Serialize(new {attachmentId=original.Id,cardId=original.CardId,version="1"}),
            JsonSerializer.Serialize(new {attachmentId=original.Id,cardId=original.CardId,version=1,sha256="forbidden"}),
            JsonSerializer.Serialize(new {attachmentId=original.Id,cardId=original.CardId,version=1,padding=new string('x',1024)})})
        {
            await using var malformed=new NpgsqlCommand("INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata) VALUES(@id,@tenant,'ATTACHMENT_SCAN',@key,@actor,'attachment-quarantine-scan','scan-shape',@payload);",admin);
            malformed.Parameters.AddWithValue("id",Guid.NewGuid()); malformed.Parameters.AddWithValue("tenant",organization);
            malformed.Parameters.AddWithValue("actor",original.UploaderId); malformed.Parameters.AddWithValue("key",$"attachment-scan/{original.Id:N}/1");
            malformed.Parameters.AddWithValue("payload",NpgsqlTypes.NpgsqlDbType.Jsonb,payload);
            try {await malformed.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("Malformed scan queue payload was admitted.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.CheckViolation) { }
        }
        foreach(var mutation in new[] {"id=gen_random_uuid()","tenant_id=gen_random_uuid()","actor_id=gen_random_uuid()","job_type='OTHER'",
            "service_identity='other'","safe_metadata=safe_metadata||'{\"sha256\":\"forbidden\"}'","idempotency_key='redirected'",
            "max_attempts=max_attempts+1","correlation_id=correlation_id||'-changed'","created_at=created_at+interval '1 second'"})
        {
            await using var session=await worker.OpenTenantSessionAsync(organization,ct);
            await using var change=new NpgsqlCommand($"UPDATE public.background_jobs SET {mutation} WHERE id=@id;",session.Connection,session.Transaction);
            change.Parameters.AddWithValue("id",claim.Id);
            try {await change.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("Worker redirected scan job identity.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.CheckViolation) { }
        }
        var unrelated=Guid.NewGuid();
        await using(var seed=new NpgsqlCommand("INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,available_at) VALUES(@id,@tenant,'CONTRACT_OTHER',@key,@actor,'contract-other','contract-other',clock_timestamp()+interval '1 day');",admin))
        {
            seed.Parameters.AddWithValue("id",unrelated); seed.Parameters.AddWithValue("tenant",organization); seed.Parameters.AddWithValue("actor",original.UploaderId);
            seed.Parameters.AddWithValue("key",unrelated.ToString("N")); await seed.ExecuteNonQueryAsync(ct);
        }
        await using(var session=await worker.OpenTenantSessionAsync(organization,ct))
        await using(var conversion=new NpgsqlCommand("UPDATE public.background_jobs SET job_type='ATTACHMENT_SCAN' WHERE id=@id;",session.Connection,session.Transaction))
        {
            conversion.Parameters.AddWithValue("id",unrelated);
            try {await conversion.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("Worker converted another capability into a scan.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.CheckViolation) { }
        }
        // API has neither private scan capability, despite normal metadata grants.
        foreach(var function in new[] {
            "SELECT * FROM public.load_attachment_scan(NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::bigint);",
            "SELECT public.finish_attachment_scan(NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::bigint,NULL::bigint,NULL::text,NULL::text);"})
        {
            await using var session=await api.OpenTenantSessionAsync(organization,ct); await using var denied=new NpgsqlCommand(function,session.Connection,session.Transaction);
            try {await denied.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("API obtained Worker scan capability.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege) { }
        }
        async Task<long> Count(string table,string predicate)
        {
            await using var query=new NpgsqlCommand($"SELECT count(*) FROM public.{table} WHERE {predicate};",admin);
            query.Parameters.AddWithValue("job",claim.Id); query.Parameters.AddWithValue("file",original.Id); query.Parameters.AddWithValue("tenant",organization);
            return (long)(await query.ExecuteScalarAsync(ct))!;
        }
        // Expire the lease AFTER tentative file/Card/event effects. The function
        // fence must undo all of them, including its sequence allocation.
        var triggerName=$"ci_scan_expire_{claim.Id:N}";
        await using(var install=new NpgsqlCommand($"""
            CREATE FUNCTION public.{triggerName}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NEW.event_id='{claim.Id:D}'::uuid THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=NEW.event_id AND tenant_id=NEW.tenant_id;
             END IF; RETURN NEW;
            END $$;
            CREATE TRIGGER {triggerName} AFTER INSERT ON public.work_events FOR EACH ROW EXECUTE FUNCTION public.{triggerName}();
            """,admin)) await install.ExecuteNonQueryAsync(ct);
        try
        {
            try {await handler.ExecuteAsync(claim,ct);throw new InvalidOperationException("Expired Worker scan left committed effects.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.CheckViolation && error.MessageText=="Attachment scan lease fence failed") { }
            Require(await Count("attachments","tenant_id=@tenant AND id=@file AND scan_status='PENDING' AND version=1")==1,"Late lease fence retained a verdict.");
            Require(await Count("cards","tenant_id=@tenant AND id='"+original.CardId.ToString("D")+"'::uuid AND version=1")==1,"Late lease fence advanced Card revision.");
            Require(await Count("audit_events","tenant_id=@tenant AND id=@job")==0 && await Count("work_events","tenant_id=@tenant AND event_id=@job")==0,"Late lease fence retained audit/event effects.");
            Require(await Count("work_event_streams","tenant_id=@tenant AND last_sequence>0")==0,"Late lease fence retained sequence allocation.");
        }
        finally
        {
            await using var remove=new NpgsqlCommand($"DROP TRIGGER {triggerName} ON public.work_events; DROP FUNCTION public.{triggerName}();",admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        await handler.ExecuteAsync(claim,ct);
        Require(await Count("attachments","tenant_id=@tenant AND id=@file AND scan_status='CLEAN' AND version=2")==1,"Worker did not apply verified Clean status.");
        Require(await Count("audit_events","tenant_id=@tenant AND id=@job")==1 && await Count("work_events","tenant_id=@tenant AND event_id=@job AND ready_at IS NOT NULL")==1,"Worker status lacked atomic audit/ready event.");
        Require(await Count("cards","tenant_id=@tenant AND id='"+original.CardId.ToString("D")+"'::uuid AND version=2")==1,"Worker did not advance current Card revision.");
        var providerCalls=scanner.Calls; var objectReads=storage.Opens;
        Require(await delivery.LoadAsync(claim,attempt,ct) is {Status:AttachmentScanLoadStatus.Applied,Request:null},"Committed scan disclosed integrity on replay.");
        await handler.ExecuteAsync(claim,ct);
        Require(scanner.Calls==providerCalls && storage.Opens==objectReads,"Committed scan replay repeated provider I/O.");
        Require(await queue.CompleteAsync(organization,claim.Id,claim.LeaseId,workerId,ct),"Worker could not acknowledge applied scan.");
        Require(await delivery.LoadAsync(claim,attempt,ct) is {Status:AttachmentScanLoadStatus.LeaseLost,Request:null},"Completed job disclosed private integrity.");

        await AttachmentPreviewIntentContract.RunAsync(admin,api,worker,queue,organization,foreignOrganization,original,bytes,digest,ct);

        // Publish another original intent/FILE/job through the actual adapters.
        var uploads=apiServices.GetRequiredService<IAttachmentUploadIntentStore>(); var metadata=apiServices.GetRequiredService<IAttachmentMetadataStore>();
        var publisher=apiServices.GetRequiredService<IAttachmentScanJobPublisher>(); var unit=apiServices.GetRequiredService<IWorkManagementUnitOfWork>();
        async Task<Guid> PublishPending(string mime="image/png")
        {
            var at=AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow); var id=Guid.NewGuid();
            await using var parent=new NpgsqlCommand("SELECT version FROM public.cards WHERE tenant_id=@tenant AND id=@card;",admin);
            parent.Parameters.AddWithValue("tenant",organization); parent.Parameters.AddWithValue("card",original.CardId);
            var revision=(long)(await parent.ExecuteScalarAsync(ct))!;
            var value=AttachmentUploadIntent.Prepare(id,organization,original.CardId,original.UploaderId,Guid.NewGuid(),revision,"Worker retry fixture",bytes.Length,digest,at.AddHours(1),at);
            var result=await unit.ExecuteReadAsync(organization,null,"worker_fixture",()=>Task.FromResult(true),async()=>
            {
                Require(await uploads.PrepareUploadAsync(value,ct) is not null,"Retry fixture intent was not prepared."); var nonce=Guid.NewGuid();
                await uploads.TryChangeUploadAsync(organization,value.CardId,value.UploaderId,id,1,new(AttachmentUploadAction.StartWrite,at,nonce,at.AddMinutes(5)),ct);
                await uploads.TryChangeUploadAsync(organization,value.CardId,value.UploaderId,id,2,new(AttachmentUploadAction.RecordStored,at,nonce,Measured:new(new(organization,id),bytes.Length,digest),VerifiedMimeType:mime),ct);
                await metadata.CreateFileAttachmentAsync(new(new(organization,id),bytes.Length,digest),value.CardId,value.UploaderId,value.DisplayName,mime,at,ct);
                var committed=await uploads.TryChangeUploadAsync(organization,value.CardId,value.UploaderId,id,3,new(AttachmentUploadAction.Publish,at),ct);
                var file=await metadata.FindFileAttachmentAsync(organization,value.CardId,id,ct);
                Require(committed is not null && file is not null && await publisher.PublishScanAsync(committed,file,value.UploaderId,"scan-worker-retry",ct),"Retry fixture scan publication failed.");
                return WorkOperation<Guid>.Success(id);
            },ct);
            Require(result.Succeeded,"Retry fixture transaction failed."); return id;
        }
        var retryFile=await PublishPending(); scanner.Verdict=AttachmentScannerVerdict.Unavailable;
        for(var number=1;number<=5;number++)
        {
            var retry=await queue.ClaimAsync(organization,workerId,ct); Require(retry is not null && retry.AttemptCount==number,"Bounded scan retry claim failed.");
            if(number<5)
            {
                try {await handler.ExecuteAsync(retry!,ct);throw new InvalidOperationException("Unavailable scanner produced a terminal verdict before its final attempt.");}
                catch(InvalidOperationException error) when(error.Message=="Attachment scan delivery is unavailable.") { }
                await using var pending=new NpgsqlCommand("SELECT scan_status='PENDING' AND version=1 FROM public.attachments WHERE tenant_id=@tenant AND id=@file;",admin);
                pending.Parameters.AddWithValue("tenant",organization); pending.Parameters.AddWithValue("file",retryFile);
                Require(await pending.ExecuteScalarAsync(ct) is true,"Retry mutated quarantine metadata.");
                Require(await queue.FailAsync(organization,retry!.Id,retry.LeaseId,workerId,"job_handler_failed",ct),"Retry lost queue lease acknowledgment.");
                await using var advance=new NpgsqlCommand("UPDATE public.background_jobs SET available_at=clock_timestamp()-interval '1 second' WHERE id=@job AND tenant_id=@tenant;",admin);
                advance.Parameters.AddWithValue("job",retry.Id); advance.Parameters.AddWithValue("tenant",organization); await advance.ExecuteNonQueryAsync(ct);
            }
            else
            {
                await handler.ExecuteAsync(retry!,ct); Require(await queue.CompleteAsync(organization,retry!.Id,retry.LeaseId,workerId,ct),"Final failed scan could not be acknowledged.");
            }
        }
        await using(var terminal=new NpgsqlCommand("SELECT scan_status='FAILED' AND version=2 FROM public.attachments WHERE tenant_id=@tenant AND id=@file;",admin))
        {
            terminal.Parameters.AddWithValue("tenant",organization); terminal.Parameters.AddWithValue("file",retryFile);
            Require(await terminal.ExecuteScalarAsync(ct) is true,"Final unavailable scan was not retained as Failed.");
        }
        var rejectedFile=await PublishPending(); scanner.Verdict=AttachmentScannerVerdict.Infected;
        var infectedJob=await queue.ClaimAsync(organization,workerId,ct); Require(infectedJob is not null,"Malware fixture was not claimed.");
        await handler.ExecuteAsync(infectedJob!,ct);
        await using(var rejected=new NpgsqlCommand("SELECT scan_status='REJECTED' AND version=2 FROM public.attachments WHERE tenant_id=@tenant AND id=@file;",admin))
        {
            rejected.Parameters.AddWithValue("tenant",organization); rejected.Parameters.AddWithValue("file",rejectedFile);
            Require(await rejected.ExecuteScalarAsync(ct) is true,"Malware verdict was not retained as Rejected.");
        }
        Require(await queue.CompleteAsync(organization,infectedJob!.Id,infectedJob.LeaseId,workerId,ct),"Rejected scan could not be acknowledged.");
        var removedFile=await PublishPending(); var removedJob=await queue.ClaimAsync(organization,workerId,ct);
        Require(removedJob is not null,"Tombstone fixture was not claimed.");
        await using(var session=await api.OpenTenantSessionAsync(organization,ct))
        await using(var tombstone=new NpgsqlCommand("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE tenant_id=@tenant AND id=@file; UPDATE public.attachments SET lifecycle_state='DELETED',deleted_by=uploader_id,deleted_at=GREATEST(statement_timestamp(),updated_at),updated_at=GREATEST(statement_timestamp(),updated_at),version=version+1 WHERE tenant_id=@tenant AND id=@file;",session.Connection,session.Transaction))
        {
            tombstone.Parameters.AddWithValue("tenant",organization); tombstone.Parameters.AddWithValue("file",removedFile);
            Require(await tombstone.ExecuteNonQueryAsync(ct)==2,"Archive/delete fixture did not deactivate file metadata."); await session.CommitAsync(ct);
        }
        providerCalls=scanner.Calls; objectReads=storage.Opens;
        Require(await delivery.LoadAsync(removedJob!,AttachmentScanAttempt.Parse(removedJob!.SafeMetadataJson),ct) is {Status:AttachmentScanLoadStatus.Superseded,Request:null},"Deactivated file disclosed private integrity.");
        await handler.ExecuteAsync(removedJob!,ct);
        Require(scanner.Calls==providerCalls && storage.Opens==objectReads,"Deactivated file was read by the scanner.");
        Require(await queue.CompleteAsync(organization,removedJob!.Id,removedJob.LeaseId,workerId,ct),"Superseded scan could not be acknowledged.");
        await AttachmentPreviewActivationContract.RunAsync(admin,worker,queue,mime=>PublishPending(mime),organization,original.CardId,bytes,apiServices,ct);
        await AttachmentScanRecoveryContract.RunAsync(admin,worker,api,organization,original.CardId,mime=>PublishPending(mime),
            claim=>new AttachmentScanDeliveryHandler(new PostgresAttachmentScanDeliveryStore(worker),new(storage,new FixtureScanner())).ExecuteAsync(claim,ct),
            ()=>storage.Opens,ct);
        await AttachmentLifecycleContract.RunAsync(admin,api,organization,original.CardId,ct);
        Console.WriteLine("Restricted C# Worker scan: private admission, immutable claims, late lease rollback, status/Card/audit/event effects, replay without provider I/O and bounded failure passed.");
    }
    private sealed class FixtureStorage(Guid organization,byte[] bytes) : IAttachmentObjectStorage
    {
        public int Opens {get;private set;}
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();if(reference.OrganizationId!=organization)throw new InvalidOperationException("Fixture object scope widened.");Opens++;return Task.FromResult<Stream?>(new MemoryStream(bytes,false));}
        public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference,Stream source,long maximum,CancellationToken ct)=>throw new InvalidOperationException("Scan cannot upload.");
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference,CancellationToken ct)=>throw new InvalidOperationException("Scan cannot delete.");
    }
    private sealed class FixtureScanner : IAttachmentMalwareScanner
    {
        public int Calls {get;private set;} public AttachmentScannerVerdict Verdict {get;set;}=AttachmentScannerVerdict.Clean;
        public async Task<AttachmentScannerVerdict> ScanAsync(Stream source,CancellationToken ct) {Calls++;await source.CopyToAsync(Stream.Null,ct);return Verdict;}
    }
}
