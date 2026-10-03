using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Identity;

internal static class AttachmentPreviewActivationContract
{
    internal static async Task RunAsync(NpgsqlConnection admin,PostgresConnectionFactory worker,PostgresBackgroundJobStore capable,
        Func<string,Task<Guid>> publish,Guid organization,Guid card,byte[] bytes,ServiceProvider apiServices,CancellationToken ct)
    {
        void Require(bool condition,string invariant) {if(!condition)throw new InvalidOperationException(invariant);}
        var legacy=new PostgresBackgroundJobStore(worker); var workerId=Guid.NewGuid();
        var source=await publish("image/png"); var objects=new Objects(organization,source,bytes);
        var scanStore=new PostgresAttachmentScanDeliveryStore(worker,previewEnabled:true);
        var scanner=new Scanner(); var scanHandler=new AttachmentScanDeliveryHandler(scanStore,new(objects,scanner));
        var claim=await legacy.ClaimAsync(organization,workerId,ct);
        Require(claim?.JobType==AttachmentScanJobs.Type,"Default Worker did not preserve legacy scan claims.");
        var job=claim!; var attempt=AttachmentScanAttempt.Parse(job.SafeMetadataJson);
        Require(attempt.AttachmentId==source,"Activation fixture claimed a different scan.");
        async Task<T> Scalar<T>(string sql)
        {
            await using var query=new NpgsqlCommand(sql,admin);
            query.Parameters.AddWithValue("tenant",organization); query.Parameters.AddWithValue("file",source);
            query.Parameters.AddWithValue("card",card); query.Parameters.AddWithValue("job",job.Id);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        const string previews="SELECT count(*) FROM public.background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text;";
        var cardVersion=await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;");
        var sequence=await Scalar<long>("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card AND tenant_id=@tenant);");
        var trigger=$"ci_preview_queue_expire_{job.Id:N}";
        await using(var install=new NpgsqlCommand($"""
            CREATE FUNCTION public.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NEW.job_type='ATTACHMENT_PREVIEW' AND NEW.safe_metadata->>'attachmentId'='{source:D}' THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{job.Id:D}' AND tenant_id=NEW.tenant_id;
             END IF; RETURN NEW;
            END $$;
            CREATE TRIGGER {trigger} AFTER INSERT ON public.background_jobs FOR EACH ROW EXECUTE FUNCTION public.{trigger}();
            """,admin))await install.ExecuteNonQueryAsync(ct);
        try
        {
            try {await scanHandler.ExecuteAsync(job,ct);throw new InvalidOperationException("Late preview queue lease loss committed scan effects.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.CheckViolation && error.MessageText=="Attachment preview queue lease fence failed") { }
            Require(await Scalar<bool>("SELECT scan_status='PENDING' AND version=1 FROM public.attachments WHERE id=@file AND tenant_id=@tenant;"),"Queue fence retained Clean verdict.");
            Require(await Scalar<long>(previews)==0,"Queue fence retained a preview job.");
            Require(await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;")==cardVersion,"Queue fence retained Card revision.");
            Require(await Scalar<long>("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card AND tenant_id=@tenant);")==sequence,"Queue fence consumed event sequence.");
            Require(await Scalar<long>("SELECT count(*) FROM public.audit_events WHERE id=@job AND tenant_id=@tenant;")==0
                && await Scalar<long>("SELECT count(*) FROM public.work_events WHERE event_id=@job AND tenant_id=@tenant;")==0,"Queue fence retained audit/event.");
        }
        finally
        {
            await using var remove=new NpgsqlCommand($"DROP TRIGGER {trigger} ON public.background_jobs; DROP FUNCTION public.{trigger}();",admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        await scanHandler.ExecuteAsync(job,ct); Require(await Scalar<long>(previews)==1,"Clean scan did not atomically enqueue preview.");
        var beforeReads=objects.Reads;
        await scanHandler.ExecuteAsync(job,ct);
        Require(await Scalar<long>(previews)==1 && objects.Reads==beforeReads,"Scan replay duplicated preview publication/provider I/O.");
        Require(await capable.CompleteAsync(organization,job.Id,job.LeaseId,workerId,ct),"Scan fixture could not acknowledge completion.");
        var expired=Guid.NewGuid();
        await using(var seed=new NpgsqlCommand("""
            INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,
             state,attempt_count,lease_id,worker_id,lease_expires_at)
            VALUES(@id,@tenant,'ATTACHMENT_PREVIEW',@key,@actor,'attachment-private-preview','old-preview-scope',
             jsonb_build_object('attachmentId',@file::uuid,'cardId',@card::uuid,'version',3),
             'RUNNING',5,gen_random_uuid(),gen_random_uuid(),clock_timestamp()-interval '1 second');
            """,admin))
        {
            seed.Parameters.AddWithValue("id",expired);seed.Parameters.AddWithValue("tenant",organization);
            seed.Parameters.AddWithValue("actor",job.ActorId);seed.Parameters.AddWithValue("file",source);seed.Parameters.AddWithValue("card",card);
            seed.Parameters.AddWithValue("key",$"attachment-preview/{source:N}/3");await seed.ExecuteNonQueryAsync(ct);
        }
        // Exercise the old binary's exact SQL shape: no preview setting at all.
        await using(var session=await worker.OpenTenantSessionAsync(organization,ct))
        await using(var old=new NpgsqlCommand("SELECT count(*) FROM public.claim_background_job(@worker);",session.Connection,session.Transaction))
        {
            old.Parameters.AddWithValue("worker",Guid.NewGuid());
            Require((long)(await old.ExecuteScalarAsync(ct))! == 0,"Legacy SQL claimed a new handler type.");await session.CommitAsync(ct);
        }
        await using(var state=new NpgsqlCommand("SELECT state='RUNNING' AND attempt_count=5 AND version=1 FROM public.background_jobs WHERE id=@id;",admin))
        {
            state.Parameters.AddWithValue("id",expired);
            Require(await state.ExecuteScalarAsync(ct) is true,"Legacy SQL expired a new handler type.");
        }
        // Old/default claims leave new jobs untouched, even after the capable
        // adapter used the same pool. The context must be transaction-scoped.
        Require(await legacy.ClaimAsync(organization,Guid.NewGuid(),ct) is null,"Default Worker consumed a preview job.");
        Require(await Scalar<bool>("SELECT state='PENDING' AND attempt_count=0 FROM public.background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text AND safe_metadata->>'version'='2';"),"Default Worker changed preview attempts.");
        var preview=await capable.ClaimAsync(organization,workerId,ct);
        Require(preview?.JobType==AttachmentPreviewJobs.Type,"Capable Worker did not claim preview.");
        var reference=AttachmentPreviewAttempt.Parse(preview!.SafeMetadataJson);
        Require(reference==new AttachmentPreviewAttempt(source,card,2) && preview.ActorId==job.ActorId,"Automatic preview references were not canonical.");
        await using(var state=new NpgsqlCommand("SELECT state='FAILED' AND last_error_code='lease_expired' FROM public.background_jobs WHERE id=@id;",admin))
        {
            state.Parameters.AddWithValue("id",expired);
            Require(await state.ExecuteScalarAsync(ct) is true,"Capable Worker did not recover terminal preview expiry.");
        }
        var intents=new PostgresAttachmentPreviewIntentStore(worker);
        objects.ApiConnections=apiServices.GetRequiredService<PostgresConnectionFactory>();
        // The persistence-only provider deliberately has no session/Board
        // authorization services. A separate restricted read provider composes
        // real current membership/Board gates, with explicitly synthetic actor
        // proof. Never replace the original fixture's no-actor sentinel.
        var readServices=new ServiceCollection();var runtime=new RuntimeDescriptor(RuntimeMode.Production,"contract","contract");
        readServices.AddLogging();readServices.AddSingleton(objects.ApiConnections);
        readServices.AddSingleton<IClock,SystemClock>();readServices.AddSingleton<ICommandActorAuthorization>(new ReadActor(job.ActorId));
        readServices.AddSingleton<IWorkCommandContext,ReadContext>();readServices.AddSingleton<PostgresBackgroundJobStore>();
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"]="contract",
            ["STRATAAI_AUTH_RETRY_KEYS"]=JsonSerializer.Serialize(new Dictionary<string,string>{["contract"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))})
        }).Build();
        readServices.AddStrataAiIdentity(configuration,runtime);readServices.AddStrataAiOrganizations(runtime);readServices.AddStrataAiWorkManagement(runtime);
        await using var readProvider=readServices.BuildServiceProvider();
        await using(var grant=new NpgsqlCommand("""
            INSERT INTO public.users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
            VALUES(@owner,'preview-owner-'||@owner::text||'@example.test',upper('preview-owner-'||@owner::text||'@example.test'),
                'Preview read fixture owner','ACTIVE',true,'unused-contract-hash',clock_timestamp(),clock_timestamp());
            INSERT INTO public.organization_members(id,tenant_id,user_id,role,status)
            VALUES(@owner,@tenant,@owner,'OWNER','ACTIVE');
            UPDATE public.organizations SET owner_user_id=@owner WHERE id=@tenant AND owner_user_id IS NULL;
            INSERT INTO public.board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
            SELECT gen_random_uuid(),tenant_id,board_id,@actor,'MEMBER','ACTIVE',clock_timestamp(),clock_timestamp()
            FROM public.cards WHERE id=@card AND tenant_id=@tenant ON CONFLICT(board_id,user_id) DO NOTHING;
            """,admin))
        {
            grant.Parameters.AddWithValue("actor",job.ActorId);grant.Parameters.AddWithValue("card",card);grant.Parameters.AddWithValue("tenant",organization);
            grant.Parameters.AddWithValue("owner",Guid.NewGuid());
            await grant.ExecuteNonQueryAsync(ct);
        }
        var admission=readProvider.GetRequiredService<AttachmentDownloadAdmissionService>();
        var loadedPreview=await intents.LoadAsync(preview,reference,ct);
        var declaredBytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        Require(loadedPreview.Source is not null && await intents.DeclareAsync(preview,reference,loadedPreview.Source,"image/png",
            new(declaredBytes.Length,Convert.ToHexStringLower(SHA256.HashData(declaredBytes)),1,1),ct)==AttachmentPreviewDeclaration.Declared,
            "Read fixture could not declare an unpublished private preview.");
        var reads=objects.Reads;
        Require(!(await admission.AdmitPreviewAsync(card,source,job.ActorId,ct)).Succeeded && objects.Reads==reads,
            "Declared-only Clean source admitted preview bytes.");
        var generator=new Generator();
        var handler=new AttachmentPreviewDeliveryHandler(intents,new(intents,new PrivateAttachmentDownloadPreparer(objects),generator,objects),intents);
        await handler.ExecuteAsync(preview,ct);
        Require(objects.Writes==1 && generator.Calls==1,"Automatic preview did not execute staged delivery.");
        beforeReads=objects.Reads;
        await handler.ExecuteAsync(preview,ct);
        Require(objects.Reads==beforeReads && objects.Writes==1 && generator.Calls==1,"Automatic preview replay repeated provider I/O.");
        Require(await capable.CompleteAsync(organization,preview.Id,preview.LeaseId,workerId,ct),"Preview could not acknowledge publication.");
        Require(await Scalar<bool>("SELECT scan_status='CLEAN' AND version=3 FROM public.attachments WHERE id=@file AND tenant_id=@tenant;"),"Automatic preview did not publish File revision.");
        var previewRead=new AttachmentPreviewReadService(admission,new PrivateAttachmentDownloadPreparer(objects));
        var admitted=await admission.AdmitPreviewAsync(card,source,job.ActorId,ct);
        Require(admitted.Value is not null && admitted.Value.Preview.Integrity.Reference==AttachmentObjectReference.ForPreview(organization,preview.Id),
            "Restricted API did not derive the published private preview namespace.");
        reads=objects.Reads;
        Require(!(await previewRead.PrepareAsync(card,source,Guid.NewGuid(),ct)).Succeeded
            && !(await previewRead.PrepareAsync(Guid.NewGuid(),source,job.ActorId,ct)).Succeeded
            && !(await previewRead.PrepareAsync(card,source,job.ActorId,ct,2)).Succeeded && objects.Reads==reads,
            "Foreign actor/Card/stale File revision reached the preview provider.");
        var content=await previewRead.PrepareAsync(card,source,job.ActorId,ct,3);
        Require(content.Value is not null && content.Value.Admission.Preview==admitted.Value!.Preview,"Published preview was not currently authorized.");
        await using(var owned=content.Value!)
        {
            using var copied=new MemoryStream();await owned.Bytes.CopyToAsync(copied,ct);
            Require(copied.ToArray().SequenceEqual(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==")),
                "Preview read delivered different or original bytes.");
        }
        objects.Corrupt=true;
        Require((await previewRead.PrepareAsync(card,source,job.ActorId,ct)).ErrorCode=="work_storage_unavailable","Corrupt preview passed full integrity staging.");
        objects.Corrupt=false;
        objects.AfterRead=async ()=>await Scalar<int>("UPDATE public.attachments SET version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;");
        Require(!(await previewRead.PrepareAsync(card,source,job.ActorId,ct)).Succeeded,"File revision changed during preview staging retained its grant.");
        Require(!(await admission.RevalidatePreviewAsync(admitted.Value!,job.ActorId,ct)).Succeeded,"Stale preview snapshot remained authorized.");
        var renamed=await admission.AdmitPreviewAsync(card,source,job.ActorId,ct);
        Require(renamed.Succeeded,"Unchanged immutable source could not re-admit a later File revision.");
        async Task<Guid> PublishLegacyClean()
        {
            var file=await publish("image/png"); objects.Add(file,bytes);
            var oldScan=await legacy.ClaimAsync(organization,workerId,ct);
            Require(oldScan?.JobType==AttachmentScanJobs.Type,"Legacy Clean backfill source was not claimed.");
            await new AttachmentScanDeliveryHandler(new PostgresAttachmentScanDeliveryStore(worker),
                new AttachmentQuarantineScanner(objects,new Scanner())).ExecuteAsync(oldScan!,ct);
            Require(await capable.CompleteAsync(organization,oldScan!.Id,oldScan.LeaseId,workerId,ct),"Legacy Clean source was not acknowledged.");
            return file;
        }
        await AttachmentPreviewBackfillContract.RunAsync(admin,worker,objects.ApiConnections!,organization,source,PublishLegacyClean,ct);
        await CardCoverPersistenceContract.RunAsync(admin,objects.ApiConnections!,organization,card,source,ct);
        Require(!(await admission.AdmitPreviewAsync(card,source,job.ActorId,ct,archiveReview:true)).Succeeded,
            "Archive review admitted an Active source.");
        await Scalar<int>("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;");
        reads=objects.Reads;
        Require(!(await previewRead.PrepareAsync(card,source,job.ActorId,ct)).Succeeded && objects.Reads==reads,
            "Archived source retained ordinary preview delivery.");
        var archived=await admission.AdmitPreviewAsync(card,source,job.ActorId,ct,archiveReview:true);
        Require(archived.Value is not null && archived.Value.Preview==renamed.Value!.Preview,
            "Archive review lost the original committed immutable preview receipt.");
        Require(!(await previewRead.PrepareAsync(card,source,Guid.NewGuid(),ct,archiveReview:true)).Succeeded
            && !(await previewRead.PrepareAsync(card,source,job.ActorId,ct,1,archiveReview:true)).Succeeded && objects.Reads==reads,
            "Foreign/stale archive review reached provider bytes.");
        var archivedContent=await previewRead.PrepareAsync(card,source,job.ActorId,ct,archiveReview:true);
        Require(archivedContent.Value is not null,"Current protected archive review refused committed preview bytes.");
        await archivedContent.Value!.DisposeAsync();
        objects.AfterRead=async ()=>await Scalar<int>("UPDATE public.attachments SET lifecycle_state='ACTIVE',updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;");
        Require(!(await previewRead.PrepareAsync(card,source,job.ActorId,ct,archiveReview:true)).Succeeded,
            "Restoration during staging retained an archive delivery snapshot.");
        Require(!(await admission.RevalidatePreviewAsync(archived.Value!,job.ActorId,ct)).Succeeded,
            "Restored source retained an old protected archive preview grant.");
        await Scalar<int>("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant; UPDATE public.attachments SET lifecycle_state='DELETED',deleted_by=uploader_id,deleted_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=@file AND tenant_id=@tenant RETURNING 1;");
        reads=objects.Reads;
        Require(!(await previewRead.PrepareAsync(card,source,job.ActorId,ct)).Succeeded && objects.Reads==reads,"Deleted original retained preview delivery.");
        Require(!(await previewRead.PrepareAsync(card,source,job.ActorId,ct,archiveReview:true)).Succeeded && objects.Reads==reads,
            "Deleted original retained protected archive preview delivery.");
        Console.WriteLine("Restricted preview reads: committed receipt, source-bound integrity, private namespace, forced RLS, foreign actor/Card/stale revision refusal, full staging outside DB, corruption, changed revision and deletion passed.");

        async Task NoPreview(string mime,AttachmentScannerVerdict verdict)
        {
            source=await publish(mime); objects.Add(source,bytes); scanner.Verdict=verdict;
            var scan=await legacy.ClaimAsync(organization,workerId,ct);
            Require(scan?.JobType==AttachmentScanJobs.Type,"Non-image fixture did not claim scan.");
            await scanHandler.ExecuteAsync(scan!,ct);
            Require(await Scalar<long>(previews)==0,"Rejected/failed/unsupported original queued a preview.");
            Require(await capable.CompleteAsync(organization,scan!.Id,scan.LeaseId,workerId,ct),"Non-preview scan was not acknowledged.");
        }
        await NoPreview("application/pdf",AttachmentScannerVerdict.Clean);
        await NoPreview("image/png",AttachmentScannerVerdict.Infected);
        source=await publish("image/png");objects.Add(source,bytes);scanner.Verdict=AttachmentScannerVerdict.Unavailable;
        for(var number=1;number<=5;number++)
        {
            var failed=await legacy.ClaimAsync(organization,workerId,ct);
            Require(failed?.JobType==AttachmentScanJobs.Type && failed.AttemptCount==number,"Unavailable scan retry claim changed.");
            if(number<5)
            {
                try {await scanHandler.ExecuteAsync(failed!,ct);throw new InvalidOperationException("Unavailable scanner became terminal before final attempt.");}
                catch(InvalidOperationException error) when(error.Message=="Attachment scan delivery is unavailable.") { }
                Require(await capable.FailAsync(organization,failed!.Id,failed.LeaseId,workerId,"job_handler_failed",ct),"Unavailable scan lost retry lease.");
                await using var advance=new NpgsqlCommand("UPDATE public.background_jobs SET available_at=clock_timestamp()-interval '1 second' WHERE id=@id;",admin);
                advance.Parameters.AddWithValue("id",failed.Id);await advance.ExecuteNonQueryAsync(ct);
            }
            else
            {
                await scanHandler.ExecuteAsync(failed!,ct);
                Require(await capable.CompleteAsync(organization,failed!.Id,failed.LeaseId,workerId,ct),"Final failed scan could not complete.");
            }
            Require(await Scalar<long>(previews)==0,"Unavailable scan queued a preview.");
        }
        Require(await Scalar<bool>("SELECT scan_status='FAILED' AND version=2 FROM public.attachments WHERE id=@file AND tenant_id=@tenant;"),"Unavailable scanner did not retain terminal Failed.");
        Console.WriteLine("Restricted preview activation: atomic Clean enqueue, late queue lease rollback, canonical dedup, legacy/default claim and expiry exclusion, capable staged publication/replay and PDF/rejected/failed refusal passed.");
    }
    private sealed class Scanner : IAttachmentMalwareScanner
    {
        public AttachmentScannerVerdict Verdict {get;set;}=AttachmentScannerVerdict.Clean;
        public async Task<AttachmentScannerVerdict> ScanAsync(Stream source,CancellationToken ct)
        {await source.CopyToAsync(Stream.Null,ct);return Verdict;}
    }
    private sealed class ReadActor(Guid actor):ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actorId,CancellationToken cancellationToken=default)
        {cancellationToken.ThrowIfCancellationRequested();return Task.FromResult(actorId==actor);}
    }
    private sealed class ReadContext:IWorkCommandContext {public Guid? IdempotencyKey=>null;}
    private sealed class Generator : IAttachmentImagePreviewGenerator
    {
        public int Calls {get;private set;}
        public Task<AttachmentPreviewImage> GenerateAsync(AttachmentScanRequest request,string mime,Stream source,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Calls++;
            if(mime!="image/png" || !source.CanSeek || source.CanWrite)throw new InvalidOperationException("Fixture generator source was not verified/owned.");
            return Task.FromResult(new AttachmentPreviewImage(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="),1,1));
        }
    }
    private sealed class Objects(Guid tenant,Guid original,byte[] bytes) : IAttachmentObjectStorage
    {
        private readonly Dictionary<AttachmentObjectReference,byte[]> _values=new(){[new(tenant,original)]=bytes};
        public int Reads {get;private set;} public int Writes {get;private set;}
        public bool Corrupt;
        public Func<Task>? AfterRead;
        public PostgresConnectionFactory? ApiConnections;
        public void Add(Guid file,byte[] content)=>_values.Add(new(tenant,file),content);
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();if(reference.OrganizationId!=tenant)throw new InvalidOperationException("Fixture scope widened.");
            if(ApiConnections is not null && typeof(PostgresConnectionFactory).GetMethod("HasCommandScope",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
                .Invoke(ApiConnections,[tenant]) is true)throw new InvalidOperationException("Preview provider I/O held an owning API transaction.");
            Reads++;if(!_values.TryGetValue(reference,out var value))return Task.FromResult<Stream?>(null);
            var copy=value.ToArray();if(Corrupt)copy[^1]^=1;
            var callback=AfterRead;AfterRead=null;return Task.FromResult<Stream?>(new Read(copy,callback));
        }
        private sealed class Read(byte[] content,Func<Task>? closed):MemoryStream(content,false)
        {
            private int _closed;
            public override async ValueTask DisposeAsync()
            {await base.DisposeAsync();if(Interlocked.Exchange(ref _closed,1)==0 && closed is not null)await closed();}
        }
        public async Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference,Stream source,long maximum,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();if(!reference.IsPreview || reference.OrganizationId!=tenant || _values.ContainsKey(reference))throw new InvalidOperationException("Fixture write is not private non-clobbering preview.");
            using var output=new MemoryStream();await source.CopyToAsync(output,ct);var content=output.ToArray();
            if(content.Length!=maximum)throw new InvalidOperationException("Fixture output size changed.");
            _values.Add(reference,content);Writes++;return new(reference,content.Length,Convert.ToHexStringLower(SHA256.HashData(content)));
        }
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference,CancellationToken ct)=>throw new InvalidOperationException("Preview activation cannot delete uncertain objects.");
    }
}
