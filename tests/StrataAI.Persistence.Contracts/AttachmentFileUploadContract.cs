using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Actual restricted PostgreSQL commands and actual forward byte inspection.
// The object provider is deterministic fault injection, not deployed S3 proof.
internal static class AttachmentFileUploadContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }

    private sealed class Source(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool Disposed;
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException("Upload source cannot seek.");
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class ReadStream(byte[] bytes, Action closed) : MemoryStream(bytes, writable: false)
    { protected override void Dispose(bool disposing) { if (disposing) closed(); base.Dispose(disposing); } }
    private sealed class Objects(PostgresConnectionFactory connections) : IAttachmentObjectStorage
    {
        private readonly Dictionary<AttachmentObjectReference, byte[]> _bytes = new();
        public int Writes, Reads, ClosedReads, Deletes;
        public bool LoseWriteReply, FailWriteBeforeCommit, FailRead;
        public Func<Task>? AfterWrite;
        private void OutsideCommand(AttachmentObjectReference reference)
        {
            var method = typeof(PostgresConnectionFactory).GetMethod("HasCommandScope", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing scope assertion hook.");
            Require(method.Invoke(connections, [reference.OrganizationId]) is false, "Provider I/O held the owning database command transaction.");
        }
        public async Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximumBytes, CancellationToken ct)
        {
            OutsideCommand(reference); Writes++; ct.ThrowIfCancellationRequested();
            if (FailWriteBeforeCommit) { FailWriteBeforeCommit = false; throw new AttachmentStorageException("object_storage_unavailable"); }
            Require(!_bytes.ContainsKey(reference), "Recovery attempted to overwrite retained private bytes.");
            using var copied = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct); if (read == 0) break;
                if (copied.Length + read > maximumBytes) throw new AttachmentStorageException("object_too_large");
                await copied.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            var bytes = copied.ToArray(); _bytes.Add(reference, bytes);
            if (AfterWrite is not null) { var action = AfterWrite; AfterWrite = null; await action(); }
            if (LoseWriteReply) { LoseWriteReply = false; throw new AttachmentStorageException("object_storage_unavailable"); }
            ct.ThrowIfCancellationRequested();
            return new(reference, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
        {
            OutsideCommand(reference); Reads++; ct.ThrowIfCancellationRequested();
            if (FailRead) { FailRead = false; throw new AttachmentStorageException("object_storage_unavailable"); }
            return Task.FromResult<Stream?>(_bytes.TryGetValue(reference, out var bytes) ? new ReadStream(bytes, () => ClosedReads++) : null);
        }
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct)
        { OutsideCommand(reference); Deletes++; throw new InvalidOperationException("Upload recovery must retain private objects."); }
        public void Corrupt(AttachmentObjectReference reference) => _bytes[reference][^1] ^= 1;
    }

    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid actor,
        Func<Task<Guid>> cardFactory, Func<DateTimeOffset> now, Action<DateTimeOffset> setTime, CancellationToken ct)
    {
        var objects = new Objects(provider.GetRequiredService<PostgresConnectionFactory>());
        var admission = provider.GetRequiredService<AttachmentUploadAdmissionService>();
        var upload = new AttachmentFileUploadService(admission, provider.GetRequiredService<AttachmentFilePublicationService>(), objects,
            provider.GetRequiredService<AttachmentUploadPolicy>(), new AttachmentFileTypeInspector(), provider.GetRequiredService<IClock>());
        // Header classification only: these deterministic bytes do not assert
        // that a full PDF is valid, safe, scanned or previewable.
        var bytes = new byte[100003]; RandomNumberGenerator.Fill(bytes); "%PDF-1.7\n"u8.CopyTo(bytes);
        var input = new PrepareAttachmentUploadInput("Original document.pdf", bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)), 1);
        Task<WorkOperation<AttachmentChange>> Send(Guid card, Guid key, Stream source, CancellationToken token)
            => upload.UploadAsync(card, actor, key, input, source, "actual-byte-upload-contract", token);
        async Task<AttachmentUploadRecord> Snapshot(Guid card, Guid key)
        {
            var result = await admission.PrepareAsync(card, actor, key, input, ct);
            Require(result.Succeeded && result.Value is not null, "Retained upload could not be read by its current actor."); return result.Value!;
        }
        async Task Effects(Guid card, long count, long revision = 1)
        {
            await using var query = new NpgsqlCommand("""
                SELECT c.version,
                  (SELECT count(*) FROM attachments WHERE tenant_id=@tenant AND card_id=@card),
                  (SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_SCAN' AND safe_metadata->>'cardId'=@card::text),
                  (SELECT count(*) FROM audit_events a JOIN attachment_upload_intents u ON u.id=a.entity_id AND u.tenant_id=a.tenant_id
                    WHERE a.tenant_id=@tenant AND u.card_id=@card AND a.event_type='ATTACHMENT_ADDED'),
                  (SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND entity_type='Card' AND entity_id=@card),
                  (SELECT count(*) FROM work_command_replays r JOIN attachment_upload_intents u ON u.tenant_id=r.tenant_id
                    AND u.uploader_id=r.actor_id AND u.retry_key=r.key_id WHERE r.tenant_id=@tenant AND u.card_id=@card)
                FROM cards c WHERE c.id=@card;
                """, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card);
            await using var row = await query.ExecuteReaderAsync(ct); Require(await row.ReadAsync(ct), "Byte fixture Card disappeared.");
            Require(row.GetInt64(0) == revision, "Upload changed Card revision unexpectedly.");
            for (var index = 1; index <= 5; index++) Require(row.GetInt64(index) == count, "Upload effects were missing, partial or duplicated.");
        }

        var happy = await cardFactory(); var happyKey = Guid.NewGuid();
        using var original = new Source(bytes);
        var created = await Send(happy, happyKey, original, ct);
        Require(created.Succeeded && created.Value!.Attachment.ScanStatus == AttachmentScanStatus.Pending, "Actual bytes failed quarantined publication.");
        Require(original.Position == bytes.Length && !original.Disposed, "Upload did not consume full bytes or took ownership of caller stream.");
        await Effects(happy, 1, 2);
        using var untouched = new Source([]); var replay = await Send(happy, happyKey, untouched, ct);
        Require(replay.Succeeded && JsonSerializer.Serialize(replay) == JsonSerializer.Serialize(created)
            && untouched.Position == 0 && objects.Writes == 1 && objects.Reads == 0, "Published retry consumed a replacement body or repeated provider I/O.");
        await Effects(happy, 1, 2);

        var lost = await cardFactory(); var lostKey = Guid.NewGuid(); objects.LoseWriteReply = true;
        using var lostBody = new Source(bytes);
        Require((await Send(lost, lostKey, lostBody, ct)).ErrorCode == "work_storage_unavailable", "Lost write reply was published blindly.");
        var retained = await Snapshot(lost, lostKey); Require(retained.State == AttachmentUploadState.Reconcile, "Unknown commit lost its reconciliation claim.");
        await Effects(lost, 0); objects.FailRead = true;
        using var ignored = new Source([]);
        Require((await Send(lost, lostKey, ignored, ct)).ErrorCode == "work_storage_unavailable" && objects.Writes == 2,
            "Read outage was treated as confirmed absence or allowed another writer.");
        Require(await Snapshot(lost, lostKey) == retained, "Read outage changed the original reconciliation state.");
        var recovered = await Send(lost, lostKey, ignored, ct);
        Require(recovered.Succeeded && recovered.Value!.Attachment.Id == retained.Id && ignored.Position == 0
            && objects.Writes == 2 && objects.ClosedReads == 1, "Lost reply did not recover fully measured original private bytes without another writer.");
        await Effects(lost, 1, 2);

        var absent = await cardFactory(); var absentKey = Guid.NewGuid(); objects.FailWriteBeforeCommit = true;
        using var absentBody = new Source(bytes);
        Require((await Send(absent, absentKey, absentBody, ct)).ErrorCode == "work_storage_unavailable", "Absent write fault was hidden.");
        var missing = await Snapshot(absent, absentKey); Require(missing.State == AttachmentUploadState.Reconcile, "Absent unknown write bypassed reconciliation.");
        using var replacement = new Source(bytes);
        var retried = await Send(absent, absentKey, replacement, ct);
        Require(retried.Succeeded && retried.Value!.Attachment.Id == missing.Id && objects.Writes == 4,
            "Confirmed absence did not reuse the original intent for exactly one new writer.");
        await Effects(absent, 1, 2);

        var corrupt = await cardFactory(); var corruptKey = Guid.NewGuid(); objects.LoseWriteReply = true;
        using var corruptBody = new Source(bytes); await Send(corrupt, corruptKey, corruptBody, ct);
        var damaged = await Snapshot(corrupt, corruptKey); objects.Corrupt(new(tenant, damaged.Id));
        Require((await Send(corrupt, corruptKey, ignored, ct)).ErrorCode == "attachment_integrity_invalid"
            && objects.Writes == 5 && await Snapshot(corrupt, corruptKey) == damaged, "Corrupt retained bytes were published, replaced or lost their claim.");
        await Effects(corrupt, 0);

        var active = await cardFactory(); var activeKey = Guid.NewGuid(); var prepared = await Snapshot(active, activeKey);
        var writer = await admission.ClaimAsync(active, actor, prepared.Id, activeKey, prepared.Version, ct);
        Require(writer.Succeeded, "Live writer fixture could not claim.");
        var reads = objects.Reads;
        Require((await Send(active, activeKey, ignored, ct)).ErrorCode == "attachment_upload_in_progress"
            && objects.Reads == reads && objects.Writes == 5, "Live writer retry reached the object provider.");
        var beforeExpiry = now(); setTime(beforeExpiry.AddMinutes(11));
        using var expiredBody = new Source(bytes);
        var expired = await Send(active, activeKey, expiredBody, ct);
        Require(expired.Succeeded && expired.Value!.Attachment.Id == writer.Value!.Id && objects.Writes == 6, "Expired writer could not reconcile confirmed absence.");
        Require((await admission.MarkUnknownWriteAsync(active, actor, writer.Value!, ct)).ErrorCode == "attachment_upload_unavailable", "Old writer callback changed recovered publication.");
        await Effects(active, 1, 2);

        var cancelled = await cardFactory(); var cancelledKey = Guid.NewGuid(); using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        objects.AfterWrite = () => { cancellation.Cancel(); return Task.CompletedTask; };
        using var cancelledBody = new Source(bytes);
        try { await Send(cancelled, cancelledKey, cancelledBody, cancellation.Token); throw new InvalidOperationException("Caller cancellation was swallowed."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Require((await Snapshot(cancelled, cancelledKey)).State == AttachmentUploadState.Reconcile && !cancelledBody.Disposed,
            "Cancellation lost possibly committed bytes or disposed caller body.");
        await Effects(cancelled, 0);
        Require((await Send(cancelled, cancelledKey, ignored, ct)).Succeeded && objects.Writes == 7, "Cancelled commit was not recovered without rewriting.");
        await Effects(cancelled, 1, 2);
        Require(objects.Deletes == 0 && objects.ClosedReads == 3, "Recovery deleted private objects or leaked owned read streams.");

        async Task ChangeCard(Guid card, bool archive)
        {
            await using var change = new NpgsqlCommand("UPDATE cards SET version=version+1,updated_at=@at,lifecycle_state=@state WHERE id=@card;", admin);
            change.Parameters.AddWithValue("card", card); change.Parameters.AddWithValue("at", now());
            change.Parameters.AddWithValue("state", archive ? "ARCHIVED" : "ACTIVE"); await change.ExecuteNonQueryAsync(ct);
        }
        var advanced = await cardFactory(); var advancedKey = Guid.NewGuid();
        objects.AfterWrite = () => ChangeCard(advanced, false); using var advancedBody = new Source(bytes);
        Require((await Send(advanced, advancedKey, advancedBody, ct)).ErrorCode == "version_conflict"
            && (await Snapshot(advanced, advancedKey)).State == AttachmentUploadState.Stored, "Post-stream Card revision rebased publication or lost measured private bytes.");
        await Effects(advanced, 0, 2);
        var archived = await cardFactory(); var archivedKey = Guid.NewGuid(); objects.AfterWrite = () => ChangeCard(archived, true);
        using var archivedBody = new Source(bytes);
        Require((await Send(archived, archivedKey, archivedBody, ct)).ErrorCode == "card_not_found", "Archived parent accepted post-stream measurement/publication.");
        var priorWrites = objects.Writes; var priorReads = objects.Reads;
        Require((await Send(archived, archivedKey, ignored, ct)).ErrorCode == "card_not_found"
            && objects.Writes == priorWrites && objects.Reads == priorReads, "Archived retry reached private provider bytes.");
        await Effects(archived, 0, 2);
        await using (var state = new NpgsqlCommand("SELECT status FROM attachment_upload_intents WHERE tenant_id=@tenant AND card_id=@card;", admin))
        {
            state.Parameters.AddWithValue("tenant", tenant); state.Parameters.AddWithValue("card", archived);
            Require(await state.ExecuteScalarAsync(ct) is "WRITING", "Refused cleanup discarded an expiring tracked writer.");
        }
        Require(objects.Deletes == 0, "Post-stream refusal deleted a retained private object.");
        Console.WriteLine("Restricted actual-byte upload: full prefix/hash/size, provider outside transactions, private replay, lost replies/read outages/absence/corruption, live/expired writer fencing and cancellation recovery passed.");
    }
}
