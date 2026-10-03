using Npgsql;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

internal static class AttachmentPreviewBackfillContract
{
    internal static async Task RunAsync(NpgsqlConnection admin,PostgresConnectionFactory worker,PostgresConnectionFactory api,
        Guid tenant,Guid publishedSource,Func<Task<Guid>> publishLegacyClean,CancellationToken ct)
    {
        void Require(bool condition,string invariant) {if(!condition)throw new InvalidOperationException(invariant);}
        var store=new PostgresAttachmentPreviewBackfillStore(worker);
        async Task<T> Scalar<T>(string sql,Guid? file=null)
        {
            await using var query=new NpgsqlCommand(sql,admin);
            query.Parameters.AddWithValue("tenant",tenant);query.Parameters.AddWithValue("file",file??publishedSource);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        foreach(var factory in new[]{api,worker})
        {
            await using var session=await factory.OpenTenantSessionAsync(tenant,ct);
            await using var denied=new NpgsqlCommand("SELECT count(*) FROM public.attachment_preview_sweeps;",session.Connection,session.Transaction);
            try {await denied.ExecuteScalarAsync(ct);throw new InvalidOperationException("Runtime read the private preview sweep cursor.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using(var session=await api.OpenTenantSessionAsync(tenant,ct))
        await using(var denied=new NpgsqlCommand("SELECT * FROM public.enqueue_attachment_preview_backfill(@tenant,32);",session.Connection,session.Transaction))
        {
            denied.Parameters.AddWithValue("tenant",tenant);
            try {await denied.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("API acquired Worker preview maintenance.");}
            catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using(var session=await worker.OpenTenantSessionAsync(tenant,ct))
        await using(var mismatch=new NpgsqlCommand("SELECT visited=0 AND enqueued=0 FROM public.enqueue_attachment_preview_backfill(@foreign,32);",session.Connection,session.Transaction))
        {
            mismatch.Parameters.AddWithValue("foreign",Guid.NewGuid());
            Require(await mismatch.ExecuteScalarAsync(ct) is true,"Foreign preview maintenance crossed tenant context.");
            await session.CommitAsync(ct);
        }
        foreach(var limit in new[]{0,33})
        {
            try {await store.EnqueuePageAsync(tenant,limit,ct);throw new InvalidOperationException("Unbounded preview sweep was accepted.");}
            catch(ArgumentOutOfRangeException) { }
        }
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try {await store.EnqueuePageAsync(tenant,32,cancelled.Token);throw new InvalidOperationException("Cancelled preview sweep opened DB work.");}
            catch(OperationCanceledException) { }
        }
        // These are real upload publication + legacy scan transactions, using
        // the explicit fixture provider/scanner. Older Workers did not enqueue
        // a preview, so recovery must discover more than one bounded page.
        var files=new List<Guid>();
        for(var index=0;index<36;index++)files.Add(await publishLegacyClean());
        Require(await Scalar<long>("SELECT count(*) FROM public.background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text;",files[^1])==0,
            "Legacy Clean fixture unexpectedly queued a preview.");
        await Scalar<long>("DELETE FROM public.attachment_preview_sweeps WHERE tenant_id=@tenant; SELECT 1::bigint;");
        // Withdraw the actual parent lifecycle gate without changing immutable
        // source identity. Clean rows remain in the index and advance the page.
        await Scalar<long>("UPDATE public.cards SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE tenant_id=@tenant AND id=(SELECT card_id FROM public.attachments WHERE id=@file); SELECT 1::bigint;");
        var cardVersion=await Scalar<long>("SELECT version FROM public.cards WHERE tenant_id=@tenant AND id=(SELECT card_id FROM public.attachments WHERE id=@file);");
        var events=await Scalar<long>("SELECT count(*) FROM public.work_events WHERE tenant_id=@tenant;");
        var fileVersion=await Scalar<long>("SELECT version FROM public.attachments WHERE tenant_id=@tenant AND id=@file;");
        var first=await store.EnqueuePageAsync(tenant,32,ct);
        Require(first.Visited==32 && first.Enqueued<=32,"Preview sweep failed its finite page bound.");
        // A second replica cannot advance an already owned durable cursor.
        await using(var held=await admin.BeginTransactionAsync(ct))
        {
            await using var gate=new NpgsqlCommand("SELECT tenant_id FROM public.attachment_preview_sweeps WHERE tenant_id=@tenant FOR UPDATE;",admin,held);
            gate.Parameters.AddWithValue("tenant",tenant);await gate.ExecuteScalarAsync(ct);
            Require(await store.EnqueuePageAsync(tenant,32,ct)==new StrataAI.Application.WorkManagement.AttachmentPreviewBackfillResult(0,0),
                "Concurrent Worker advanced an owned sweep cursor.");
            await held.RollbackAsync(ct);
        }
        for(var pass=0;pass<8;pass++)await store.EnqueuePageAsync(tenant,32,ct);
        foreach(var file in files)
        {
            Require(await Scalar<long>("SELECT count(*) FROM public.background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text;",file)==0,
                "Archived source parent acquired preview work.");
        }
        Require(await Scalar<long>("SELECT count(*) FROM public.background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text AND (safe_metadata->>'version')::bigint=(SELECT version FROM public.attachments WHERE id=@file);")==0,
            "Still-current published derivative was regenerated after a metadata revision.");
        Require(await Scalar<long>("SELECT version FROM public.cards WHERE tenant_id=@tenant AND id=(SELECT card_id FROM public.attachments WHERE id=@file);")==cardVersion
            && await Scalar<long>("SELECT count(*) FROM public.work_events WHERE tenant_id=@tenant;")==events
            && await Scalar<long>("SELECT version FROM public.attachments WHERE tenant_id=@tenant AND id=@file;")==fileVersion,
            "Metadata maintenance altered Card/File/event publication.");
        // Restored sources behind the cursor are recovered on wrap.
        await Scalar<long>("UPDATE public.cards SET lifecycle_state='ACTIVE',archived_at=NULL,updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE tenant_id=@tenant AND id=(SELECT card_id FROM public.attachments WHERE id=@file); SELECT 1::bigint;");
        for(var pass=0;pass<8;pass++)await store.EnqueuePageAsync(tenant,32,ct);
        foreach(var file in files)
        {
            Require(await Scalar<long>("SELECT count(*) FROM public.background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text AND safe_metadata->>'version'='2' AND actor_id=(SELECT uploader_id FROM public.attachments WHERE id=@file) AND service_identity='attachment-private-preview';",file)==1,
                "Later restored legacy Clean source was starved or duplicated.");
        }
        Require(await Scalar<long>("SELECT count(*) FROM public.background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text AND (safe_metadata->>'version')::bigint=(SELECT version FROM public.attachments WHERE id=@file);")==0,
            "Active restored parent regenerated a still-current published derivative.");
        Require(await Scalar<long>("SELECT version FROM public.cards WHERE tenant_id=@tenant AND id=(SELECT card_id FROM public.attachments WHERE id=@file);")==cardVersion+1
            && await Scalar<long>("SELECT count(*) FROM public.work_events WHERE tenant_id=@tenant;")==events
            && await Scalar<long>("SELECT version FROM public.attachments WHERE tenant_id=@tenant AND id=@file;")==fileVersion,
            "Restored metadata maintenance altered publication revisions/events.");
        // Failed jobs retain their exact terminal row and retry identity.
        await Scalar<long>("UPDATE public.background_jobs SET state='FAILED',attempt_count=max_attempts,last_error_code='lease_expired',updated_at=clock_timestamp(),version=version+1 WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text; SELECT 1::bigint;",files[1]);
        const string failedSql="SELECT to_jsonb(j)::text FROM public.background_jobs j WHERE tenant_id=@tenant AND job_type='ATTACHMENT_PREVIEW' AND safe_metadata->>'attachmentId'=@file::text;";
        var failed=await Scalar<string>(failedSql,files[1]);
        for(var pass=0;pass<8;pass++)await store.EnqueuePageAsync(tenant,32,ct);
        Require(await Scalar<string>(failedSql,files[1])==failed,"Backfill reset or rewrote a terminal preview job.");
        Console.WriteLine("Restricted preview backfill: Worker-only tenant scope, private forced-RLS cursor, bounded indexed pages, replica skip-lock, canonical legacy Clean jobs, receipt reuse, parent archival/restoration on wrap, cancellation and unchanged terminal failures passed.");
    }
}
