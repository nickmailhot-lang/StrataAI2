using System.Text.Json;
using System.Security.Cryptography;
using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

internal static class AttachmentPreviewIntentContract
{
    internal static async Task RunAsync(NpgsqlConnection admin, PostgresConnectionFactory api, PostgresConnectionFactory worker,
        PostgresBackgroundJobStore queue, Guid organization, Guid foreignOrganization, AttachmentUploadIntent original,
        byte[] bytes, string digest, CancellationToken ct)
    {
        void Require(bool condition, string invariant) { if (!condition) throw new InvalidOperationException(invariant); }
        var id = Guid.NewGuid(); var actor = original.UploaderId;
        var payload = JsonSerializer.Serialize(new { attachmentId = original.Id, cardId = original.CardId, version = 2 });
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
            VALUES(@id,@tenant,'ATTACHMENT_PREVIEW',@key,@actor,'attachment-private-preview','preview-contract',@payload);
            """, admin))
        {
            seed.Parameters.AddWithValue("id", id); seed.Parameters.AddWithValue("tenant", organization);
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("key", $"attachment-preview/{original.Id:N}/2");
            seed.Parameters.AddWithValue("payload", NpgsqlTypes.NpgsqlDbType.Jsonb, payload); await seed.ExecuteNonQueryAsync(ct);
        }
        var claimed = await queue.ClaimAsync(organization, Guid.NewGuid(), ct);
        Require(claimed?.Id == id, "Preview fixture did not claim its exact job.");
        var job = claimed!; var attempt = AttachmentPreviewAttempt.Parse(job.SafeMetadataJson);
        var store = new PostgresAttachmentPreviewIntentStore(worker);
        var loaded = await store.LoadAsync(job, attempt, ct);
        Require(loaded.Status == AttachmentPreviewLoadStatus.Ready && loaded.Source?.SizeBytes == bytes.Length
            && loaded.Source.Sha256 == digest && loaded.VerifiedMimeType == "image/png" && loaded.DeclaredOutput is null,
            "Preview admission failed to bind canonical Clean source.");
        foreach (var forged in new[] { job with { OrganizationId = foreignOrganization }, job with { ActorId = Guid.NewGuid() },
            job with { WorkerId = Guid.NewGuid() }, job with { LeaseId = Guid.NewGuid() } })
            Require(await store.LoadAsync(forged, attempt, ct) is { Status: AttachmentPreviewLoadStatus.LeaseLost, Source: null, VerifiedMimeType: null, DeclaredOutput: null },
                "Unproven preview claim disclosed private measurements.");
        var wrongCard = job with { SafeMetadataJson = JsonSerializer.Serialize(new { attachmentId = original.Id, cardId = Guid.NewGuid(), version = 2 }) };
        Require(await store.LoadAsync(wrongCard, AttachmentPreviewAttempt.Parse(wrongCard.SafeMetadataJson), ct)
            is { Status: AttachmentPreviewLoadStatus.LeaseLost, Source: null }, "Forged preview parent was admitted.");
        foreach (var mutation in new[] { "id=gen_random_uuid()", "tenant_id=gen_random_uuid()", "actor_id=gen_random_uuid()", "job_type='OTHER'",
            "service_identity='other'", "safe_metadata=safe_metadata||'{\"sha256\":\"private\"}'", "idempotency_key='redirected'",
            "max_attempts=max_attempts+1", "correlation_id='changed'", "created_at=created_at+interval '1 second'" })
        {
            await using var session = await worker.OpenTenantSessionAsync(organization, ct);
            await using var change = new NpgsqlCommand($"UPDATE public.background_jobs SET {mutation} WHERE id=@id;", session.Connection, session.Transaction);
            change.Parameters.AddWithValue("id", id);
            try { await change.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Preview identity was redirected."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { }
        }
        foreach (var malformed in new[] { "[]", "null", "{}", payload.Replace("\"version\":2", "\"version\":1", StringComparison.Ordinal),
            payload.Replace("\"version\":2", "\"version\":9223372036854775807", StringComparison.Ordinal),
            payload.Replace("\"version\":2", "\"version\":\"2\"", StringComparison.Ordinal),
            payload.Replace("\"version\":2", "\"version\":2,\"sha256\":\"forbidden\"", StringComparison.Ordinal) })
        {
            await using var seed = new NpgsqlCommand("""
                INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
                VALUES(@id,@tenant,'ATTACHMENT_PREVIEW',@key,@actor,'attachment-private-preview','preview-shape',@payload);
                """, admin);
            seed.Parameters.AddWithValue("id", Guid.NewGuid()); seed.Parameters.AddWithValue("tenant", organization);
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("key", $"attachment-preview/{original.Id:N}/2");
            seed.Parameters.AddWithValue("payload", NpgsqlTypes.NpgsqlDbType.Jsonb, malformed);
            try { await seed.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Malformed preview identity was admitted."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { }
        }
        foreach (var factory in new[] { api, worker })
        foreach (var sql in new[] { "SELECT * FROM public.attachment_previews;", "UPDATE public.attachment_previews SET width=1;",
            "DELETE FROM public.attachment_previews;" })
        {
            await using var session = await factory.OpenTenantSessionAsync(organization, ct);
            await using var denied = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            try { await denied.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Runtime role directly accessed private preview ledger."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using (var session = await api.OpenTenantSessionAsync(organization, ct))
        await using (var denied = new NpgsqlCommand("SELECT * FROM public.load_attachment_preview(NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::uuid,NULL::bigint);", session.Connection, session.Transaction))
        {
            try { await denied.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("API acquired private preview Worker admission."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
        }

        var encoded = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        var output = new AttachmentPreviewMeasurement(encoded.Length, Convert.ToHexStringLower(SHA256.HashData(encoded)), 1, 1);
        var source = loaded.Source!;
        Require(await store.DeclareAsync(job, attempt, new(source.Reference, source.SizeBytes, new string('c', 64)), "image/png", output, ct)
            == AttachmentPreviewDeclaration.LeaseLost, "Preview declaration accepted forged source integrity.");
        async Task<long> ManifestCount()
        {
            await using var count = new NpgsqlCommand("SELECT count(*) FROM public.attachment_previews WHERE id=@id;", admin);
            count.Parameters.AddWithValue("id", id); return (long)(await count.ExecuteScalarAsync(ct))!;
        }
        Require(await ManifestCount() == 0, "Rejected preview declaration left a manifest.");

        // Force lease loss after the tentative ledger insertion. The final fence
        // must roll back the insert, even though all initial checks succeeded.
        var trigger = $"ci_preview_expire_{id:N}";
        await using (var install = new NpgsqlCommand($"""
            CREATE FUNCTION public.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NEW.id='{id:D}'::uuid THEN
              UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=NEW.id AND tenant_id=NEW.tenant_id;
             END IF; RETURN NEW;
            END $$;
            CREATE TRIGGER {trigger} AFTER INSERT ON public.attachment_previews FOR EACH ROW EXECUTE FUNCTION public.{trigger}();
            """, admin)) await install.ExecuteNonQueryAsync(ct);
        try
        {
            try { await store.DeclareAsync(job, attempt, source, "image/png", output, ct); throw new InvalidOperationException("Late preview lease loss committed a manifest."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation && error.MessageText == "Attachment preview lease fence failed") { }
            Require(await ManifestCount() == 0, "Late preview lease fence retained tentative output.");
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP TRIGGER {trigger} ON public.attachment_previews; DROP FUNCTION public.{trigger}();", admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        Require(await store.DeclareAsync(job, attempt, source, "image/png", output, ct) == AttachmentPreviewDeclaration.Declared
            && await ManifestCount() == 1, "Preview output declaration was not persisted.");
        Require(await store.DeclareAsync(job, attempt, source, "image/png", output, ct) == AttachmentPreviewDeclaration.Declared
            && await ManifestCount() == 1, "Exact preview declaration replay duplicated output.");
        Require(await store.DeclareAsync(job, attempt, source, "image/png", new(70, new string('c', 64), 1, 1), ct)
            == AttachmentPreviewDeclaration.Conflict, "Preview retry replaced declared bytes.");
        Require((await store.LoadAsync(job, attempt, ct)).DeclaredOutput == output, "Preview retry lost its private recovery measurement.");
        // Give a disposable test transaction read permission to exercise actual
        // forced RLS under a NOBYPASSRLS role, then restore production privileges.
        await using (var grant = new NpgsqlCommand("GRANT SELECT ON public.attachment_previews TO strataai_api_runtime;", admin))
            await grant.ExecuteNonQueryAsync(ct);
        try
        {
            foreach (var scope in new[] { organization, foreignOrganization })
            {
                await using var transaction = await admin.BeginTransactionAsync(ct);
                await using (var role = new NpgsqlCommand("SET LOCAL ROLE strataai_api_runtime;", admin, transaction))
                    await role.ExecuteNonQueryAsync(ct);
                await using (var tenant = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,true);", admin, transaction))
                {
                    tenant.Parameters.AddWithValue("tenant", scope.ToString("D")); await tenant.ExecuteNonQueryAsync(ct);
                }
                await using var read = new NpgsqlCommand("SELECT count(*) FROM public.attachment_previews WHERE id=@id;", admin, transaction);
                read.Parameters.AddWithValue("id", id);
                Require((long)(await read.ExecuteScalarAsync(ct))! == (scope == organization ? 1 : 0),
                    "Preview manifest crossed its forced RLS tenant boundary.");
                await transaction.RollbackAsync(ct);
            }
        }
        finally
        {
            await using var revoke = new NpgsqlCommand("REVOKE SELECT ON public.attachment_previews FROM strataai_api_runtime;", admin);
            await revoke.ExecuteNonQueryAsync(ct);
        }
        await using (var mutate = new NpgsqlCommand("UPDATE public.attachment_previews SET output_sha256=repeat('d',64) WHERE id=@id;", admin))
        {
            mutate.Parameters.AddWithValue("id", id);
            try { await mutate.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Preview manifest was rewritten."); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { }
        }
        // Current parent lifecycle gates private source and recovery disclosure.
        await using (var archive = new NpgsqlCommand("UPDATE public.cards SET lifecycle_state='ARCHIVED',archived_at=clock_timestamp() WHERE id=@card AND tenant_id=@tenant;", admin))
        {
            archive.Parameters.AddWithValue("card", original.CardId); archive.Parameters.AddWithValue("tenant", organization);
            await archive.ExecuteNonQueryAsync(ct);
        }
        try
        {
            Require(await store.LoadAsync(job, attempt, ct) is { Status: AttachmentPreviewLoadStatus.Superseded, Source: null, DeclaredOutput: null },
                "Archived preview source disclosed recovery integrity.");
        }
        finally
        {
            await using var restore = new NpgsqlCommand("UPDATE public.cards SET lifecycle_state='ACTIVE',archived_at=NULL WHERE id=@card AND tenant_id=@tenant;", admin);
            restore.Parameters.AddWithValue("card", original.CardId); restore.Parameters.AddWithValue("tenant", organization); await restore.ExecuteNonQueryAsync(ct);
        }
        await AttachmentPreviewPublicationContract.RunAsync(admin,api,worker,job,attempt,output,store,bytes,encoded,foreignOrganization,ct);
        Require(await queue.CompleteAsync(organization, id, job.LeaseId, job.WorkerId, ct), "Preview fixture could not release its claim.");
        Require(await store.LoadAsync(job, attempt, ct) is { Status: AttachmentPreviewLoadStatus.LeaseLost, Source: null, DeclaredOutput: null },
            "Completed preview claim disclosed measurements.");
        Console.WriteLine("Restricted preview intent: canonical Clean admission, private recovery, immutable identity/manifest, late lease rollback, lifecycle and role refusal passed.");
    }
}
