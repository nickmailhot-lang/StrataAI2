using Npgsql;
using System.Security.Cryptography;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

internal static class AttachmentPreviewPublicationContract
{
    internal static async Task RunAsync(NpgsqlConnection admin, PostgresConnectionFactory api, PostgresConnectionFactory worker,
        ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt, AttachmentPreviewMeasurement measurement,
        PostgresAttachmentPreviewIntentStore store, byte[] sourceBytes, byte[] encodedBytes, Guid foreignOrganization, CancellationToken ct)
    {
        void Require(bool condition, string invariant) { if (!condition) throw new InvalidOperationException(invariant); }
        var output = new AttachmentPreviewStoredOutput(AttachmentObjectReference.ForPreview(job.OrganizationId, job.Id), measurement);
        async Task<T> Scalar<T>(string sql)
        {
            await using var query = new NpgsqlCommand(sql, admin);
            query.Parameters.AddWithValue("job", job.Id); query.Parameters.AddWithValue("tenant", job.OrganizationId);
            query.Parameters.AddWithValue("file", attempt.AttachmentId); query.Parameters.AddWithValue("card", attempt.CardId);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        var cardVersion = await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;");
        var board = await Scalar<Guid>("SELECT board_id FROM public.cards WHERE id=@card AND tenant_id=@tenant;");
        var sequence = await Scalar<long>("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card AND tenant_id=@tenant);");
        async Task NoEffects()
        {
            Require(await Scalar<long>("SELECT version FROM public.attachments WHERE id=@file AND tenant_id=@tenant;") == attempt.Version,
                "Refused preview publication advanced File revision.");
            Require(await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;") == cardVersion,
                "Refused preview publication advanced Card revision.");
            Require(await Scalar<long>("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card AND tenant_id=@tenant);") == sequence,
                "Refused preview publication consumed a Board sequence.");
            foreach (var table in new[] { "attachment_preview_publications", "audit_events" })
                Require(await Scalar<long>($"SELECT count(*) FROM public.{table} WHERE id=@job AND tenant_id=@tenant;") == 0,
                    "Refused preview publication retained receipt/audit.");
            Require(await Scalar<long>("SELECT count(*) FROM public.work_events WHERE event_id=@job AND tenant_id=@tenant;") == 0,
                "Refused preview publication retained an event.");
        }
        var wrongOutput = output with { Measurement = new(measurement.SizeBytes, new string('e', 64), measurement.Width, measurement.Height) };
        try
        {
            await store.FinishAsync(job,attempt,output with {Reference=new(job.OrganizationId,job.Id)},ct);
            throw new InvalidOperationException("Preview publication accepted an original object namespace.");
        }
        catch(InvalidOperationException error) when(error.Message=="Attachment preview intent is unavailable.") { }
        Require(await store.FinishAsync(job, attempt, wrongOutput, ct) == AttachmentPreviewCompletion.LeaseLost,
            "Preview publication accepted unrecorded output.");
        await NoEffects();
        foreach (var forged in new[] { job with { LeaseId = Guid.NewGuid() }, job with { WorkerId = Guid.NewGuid() }, job with { ActorId = Guid.NewGuid() } })
        {
            Require(await store.FinishAsync(forged, attempt, output, ct) == AttachmentPreviewCompletion.LeaseLost,
                "Preview publication accepted forged claim.");
            await NoEffects();
        }
        foreach (var factory in new[] { api, worker })
        foreach (var sql in new[] { "SELECT * FROM public.attachment_preview_publications;",
            "INSERT INTO public.attachment_preview_publications(id,tenant_id,attachment_version,card_version,board_id,published_at) VALUES(gen_random_uuid(),gen_random_uuid(),3,3,gen_random_uuid(),now());",
            "UPDATE public.attachment_preview_publications SET card_version=card_version+1;",
            "DELETE FROM public.attachment_preview_publications;" })
        {
            if (factory == api && sql.StartsWith("SELECT", StringComparison.Ordinal)) continue;
            await using var session = await factory.OpenTenantSessionAsync(job.OrganizationId, ct);
            await using var denied = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            try { await denied.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Runtime accessed preview publications directly."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        }
        foreach (var sql in new[] {
            "SELECT * FROM public.load_attachment_preview_source(NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::bigint);",
            "SELECT public.finish_attachment_preview(NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::bigint,NULL::bigint,NULL::text,NULL::integer,NULL::integer);" })
        {
            await using var session = await api.OpenTenantSessionAsync(job.OrganizationId, ct);
            await using var denied = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            try { await denied.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("API obtained private preview publication capability."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using (var session = await worker.OpenTenantSessionAsync(job.OrganizationId, ct))
        await using (var denied = new NpgsqlCommand("SELECT * FROM public.load_attachment_preview_source(NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::bigint);", session.Connection, session.Transaction))
        {
            try { await denied.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Worker bypassed publication replay through private source helper."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using (var archive = new NpgsqlCommand("UPDATE public.cards SET lifecycle_state='ARCHIVED',archived_at=clock_timestamp() WHERE id=@card AND tenant_id=@tenant;", admin))
        {
            archive.Parameters.AddWithValue("card", attempt.CardId); archive.Parameters.AddWithValue("tenant", job.OrganizationId);
            await archive.ExecuteNonQueryAsync(ct);
        }
        try
        {
            Require(await store.FinishAsync(job, attempt, output, ct) == AttachmentPreviewCompletion.Superseded,
                "Preview published after current parent withdrawal.");
            await NoEffects();
        }
        finally
        {
            await using var restore = new NpgsqlCommand("UPDATE public.cards SET lifecycle_state='ACTIVE',archived_at=NULL WHERE id=@card AND tenant_id=@tenant;", admin);
            restore.Parameters.AddWithValue("card", attempt.CardId); restore.Parameters.AddWithValue("tenant", job.OrganizationId);
            await restore.ExecuteNonQueryAsync(ct);
        }

        // Expire the lease after receipt, revisions, audit and sequence/event
        // have all been tentatively written. Every effect must be rolled back.
        var trigger = $"ci_preview_publish_expire_{job.Id:N}";
        await using (var install = new NpgsqlCommand($"""
            CREATE FUNCTION public.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NEW.id='{job.Id:D}'::uuid THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=NEW.id AND tenant_id=NEW.tenant_id;
             END IF; RETURN NEW;
            END $$;
            CREATE TRIGGER {trigger} AFTER INSERT ON public.attachment_preview_publications FOR EACH ROW EXECUTE FUNCTION public.{trigger}();
            """, admin)) await install.ExecuteNonQueryAsync(ct);
        try
        {
            try { await store.FinishAsync(job, attempt, output, ct); throw new InvalidOperationException("Late preview publication lease loss committed effects."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation && error.MessageText == "Attachment preview publication lease fence failed") { }
            await NoEffects();
            Require(await Scalar<long>("SELECT count(*) FROM public.attachment_previews WHERE id=@job AND tenant_id=@tenant;") == 1,
                "Publication failure destroyed immutable storage recovery.");
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP TRIGGER {trigger} ON public.attachment_preview_publications; DROP FUNCTION public.{trigger}();", admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        // The artifact's stable Card identity follows a canonical move. The
        // publication must use the new Board, never the Board at upload time.
        var originalBoard = board; var originalSequence = sequence; var movedBoard = Guid.NewGuid(); var movedList = Guid.NewGuid();
        await using (var move = new NpgsqlCommand("""
            INSERT INTO public.boards(id,tenant_id,name,created_at,updated_at)
             VALUES(@board,@tenant,'Preview moved Board',clock_timestamp(),clock_timestamp());
            INSERT INTO public.board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
             VALUES(@list,@tenant,@board,'Preview moved List','500000000000000000000000000000',clock_timestamp(),clock_timestamp());
            UPDATE public.cards SET board_id=@board,list_id=@list,updated_at=GREATEST(updated_at,clock_timestamp()),version=version+1
             WHERE id=@card AND tenant_id=@tenant;
            INSERT INTO public.work_event_streams(tenant_id,board_id) VALUES(@tenant,@board);
            """, admin))
        {
            move.Parameters.AddWithValue("board", movedBoard); move.Parameters.AddWithValue("list", movedList);
            move.Parameters.AddWithValue("tenant", job.OrganizationId); move.Parameters.AddWithValue("card", attempt.CardId);
            await move.ExecuteNonQueryAsync(ct);
        }
        cardVersion++; board = movedBoard; sequence = 0;
        var objects = new FixtureStorage(new(job.OrganizationId,attempt.AttachmentId),sourceBytes);
        var generator = new FixtureGenerator(encodedBytes);
        var recovery = new AttachmentPreviewStorageRecovery(store,new PrivateAttachmentDownloadPreparer(objects),generator,objects);
        var handler = new AttachmentPreviewDeliveryHandler(store,recovery,store);
        await handler.ExecuteAsync(job,ct);
        Require(objects.Writes==1 && generator.Calls==1 && objects.Opens>=3,
            "Preview handler did not verify complete private source/output before publication.");
        Require(await Scalar<long>("SELECT version FROM public.attachments WHERE id=@file AND tenant_id=@tenant;") == attempt.Version + 1,
            "Preview publication did not advance File revision.");
        Require(await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;") == cardVersion + 1,
            "Preview publication did not advance Card revision.");
        Require(await Scalar<long>("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=(SELECT board_id FROM public.cards WHERE id=@card AND tenant_id=@tenant);") == sequence + 1,
            "Preview publication sequence was not contiguous.");
        Require(await Scalar<Guid>("SELECT board_id FROM public.attachment_preview_publications WHERE id=@job AND tenant_id=@tenant;") == board,
            "Preview receipt names a different Board.");
        await using (var previous = new NpgsqlCommand("SELECT last_sequence FROM public.work_event_streams WHERE tenant_id=@tenant AND board_id=@board;", admin))
        {
            previous.Parameters.AddWithValue("tenant", job.OrganizationId); previous.Parameters.AddWithValue("board", originalBoard);
            Require((long)(await previous.ExecuteScalarAsync(ct))! == originalSequence, "Preview publication advanced the old Board stream.");
        }
        Require(await Scalar<bool>("""
            SELECT a.event_type='ATTACHMENT_PREVIEW_PUBLISHED' AND a.entity_type='Attachment' AND a.entity_id=@file
             AND e.event_type='ATTACHMENT_PREVIEW_PUBLISHED' AND e.entity_type='Card' AND e.entity_id=@card
             AND r.card_version=e.entity_version AND r.published_at=e.ready_at AND r.published_at=a.created_at
            FROM public.attachment_preview_publications r JOIN public.audit_events a ON a.id=r.id AND a.tenant_id=r.tenant_id
             JOIN public.work_events e ON e.event_id=r.id AND e.tenant_id=r.tenant_id WHERE r.id=@job AND r.tenant_id=@tenant;
            """), "Preview receipt lacked atomic ready event/audit.");
        // Permanent read-only API grants exercise actual forced tenant RLS.
        {
            foreach(var scope in new[] {job.OrganizationId,foreignOrganization})
            {
                await using var transaction=await admin.BeginTransactionAsync(ct);
                await using(var role=new NpgsqlCommand("SET LOCAL ROLE strataai_api_runtime;",admin,transaction))
                    await role.ExecuteNonQueryAsync(ct);
                await using(var tenant=new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,true);",admin,transaction))
                {
                    tenant.Parameters.AddWithValue("tenant",scope.ToString("D")); await tenant.ExecuteNonQueryAsync(ct);
                }
                await using var count=new NpgsqlCommand("SELECT count(*) FROM public.attachment_preview_publications WHERE id=@job;",admin,transaction);
                count.Parameters.AddWithValue("job",job.Id);
                Require((long)(await count.ExecuteScalarAsync(ct))! == (scope==job.OrganizationId?1:0),"Published preview receipt crossed forced tenant RLS.");
                await transaction.RollbackAsync(ct);
            }
        }

        Require(await store.LoadAsync(job, attempt, ct) is { Status: AttachmentPreviewLoadStatus.Applied, Source: null, VerifiedMimeType: null, DeclaredOutput: null },
            "Committed preview replay disclosed private measurements.");
        var opens=objects.Opens;
        await handler.ExecuteAsync(job,ct);
        Require(objects.Writes==1 && generator.Calls==1 && objects.Opens==opens,"Committed preview handler replay repeated provider I/O.");
        Require(await store.FinishAsync(job, attempt, output, ct) == AttachmentPreviewCompletion.Applied,
            "Preview publication replay was not idempotent.");
        Require(await Scalar<long>("SELECT version FROM public.cards WHERE id=@card AND tenant_id=@tenant;") == cardVersion + 1
            && await Scalar<long>("SELECT count(*) FROM public.attachment_preview_publications WHERE id=@job AND tenant_id=@tenant;") == 1,
            "Preview publication replay duplicated effects.");
        foreach (var sql in new[] { "UPDATE public.attachment_preview_publications SET card_version=card_version+1 WHERE id=@job;",
            "DELETE FROM public.attachment_preview_publications WHERE id=@job;" })
        {
            await using var mutation = new NpgsqlCommand(sql, admin); mutation.Parameters.AddWithValue("job", job.Id);
            try { await mutation.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Preview publication receipt was rewritten."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { }
        }
        Console.WriteLine("Restricted preview publication: canonical output/claim/lifecycle refusal, late lease full rollback, moved current-Board event, verified staged handler delivery, revisions/audit/receipt, replay without provider I/O and role refusal passed.");
    }
    // Fixed fixture transformation; actual native decoding/containment is
    // independently required by the exact Worker image's public PNG check.
    private sealed class FixtureGenerator(byte[] encoded) : IAttachmentImagePreviewGenerator
    {
        public int Calls { get; private set; }
        public Task<AttachmentPreviewImage> GenerateAsync(AttachmentScanRequest request,string mime,Stream source,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(!source.CanRead || !source.CanSeek || source.CanWrite || source.Length!=request.SizeBytes || mime!="image/png")
                throw new InvalidOperationException("Fixture generator did not receive verified owned source.");
            Calls++; return Task.FromResult(new AttachmentPreviewImage(encoded.ToArray(),1,1));
        }
    }
    private sealed class FixtureStorage(AttachmentObjectReference original,byte[] bytes) : IAttachmentObjectStorage
    {
        private readonly Dictionary<AttachmentObjectReference,byte[]> _values=new() { [original]=bytes };
        public int Opens { get; private set; }
        public int Writes { get; private set; }
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if(reference.OrganizationId!=original.OrganizationId)throw new InvalidOperationException("Fixture scope widened.");
            Opens++; return Task.FromResult<Stream?>(_values.TryGetValue(reference,out var stored)?new MemoryStream(stored,false):null);
        }
        public async Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference,Stream source,long maximum,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(reference==original || reference.OrganizationId!=original.OrganizationId || _values.ContainsKey(reference))
                throw new InvalidOperationException("Fixture write would overwrite or widen scope.");
            using var output=new MemoryStream(); await source.CopyToAsync(output,ct); var encoded=output.ToArray();
            if(encoded.Length!=maximum)throw new InvalidOperationException("Fixture encoded size changed.");
            _values.Add(reference,encoded); Writes++;
            return new(reference,encoded.Length,Convert.ToHexStringLower(SHA256.HashData(encoded)));
        }
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference,CancellationToken ct)=>
            throw new InvalidOperationException("Preview delivery must retain uncertain artifacts.");
    }
}
