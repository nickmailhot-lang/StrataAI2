using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Real restricted PostgreSQL adapters and processor; only the private object
// and malware providers are controlled fixtures. Two small sources, no load test.
internal static class AttachmentProviderRecoveryContract
{
    internal static async Task RunAsync(NpgsqlConnection admin, string workerConnection,
        Guid tenant, Guid card, Func<Task<Guid>> publish, byte[] bytes, string digest, CancellationToken ct)
    {
        void Require(bool condition, string invariant)
        { if (!condition) throw new InvalidOperationException(invariant); }

        foreach (var storageOutage in new[] { true, false })
        {
            var file = await publish();
            async Task<T> Scalar<T>(string sql)
            {
                await using var command = new NpgsqlCommand(sql, admin);
                command.Parameters.AddWithValue("tenant", tenant);
                command.Parameters.AddWithValue("file", file);
                command.Parameters.AddWithValue("card", card);
                command.Parameters.AddWithValue("digest", digest);
                command.Parameters.AddWithValue("size", (long)bytes.Length);
                return (T)(await command.ExecuteScalarAsync(ct))!;
            }
            var job = await Scalar<Guid>("SELECT id FROM background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_SCAN' AND safe_metadata->>'attachmentId'=@file::text;");
            var metadata = await Scalar<string>($"SELECT safe_metadata::text FROM background_jobs WHERE id='{job:D}'::uuid;");
            var revision = await Scalar<long>("SELECT version FROM cards WHERE tenant_id=@tenant AND id=@card;");
            var firstStorage = new FixtureStorage(new(tenant, file), bytes, storageOutage);
            var firstScanner = new FixtureScanner(unavailable: !storageOutage);
            // Dispose this process's adapters after its committed failure. The
            // second processor must recover from database state alone.
            await using (var connections = new PostgresConnectionFactory(workerConnection))
            {
                var processor = Processor(connections, firstStorage, firstScanner);
                Require(await processor.ProcessOneAsync(tenant, Guid.NewGuid(), ct) == JobProcessingResult.Retried,
                    "Temporary attachment provider failure was not durably retried.");
            }
            Require(firstStorage.Opens == 1 && firstScanner.Calls == (storageOutage ? 0 : 1),
                "Provider outage performed unexpected private I/O.");
            Require(await Scalar<bool>($"SELECT state='PENDING' AND attempt_count=1 AND last_error_code='job_handler_failed' AND lease_id IS NULL AND worker_id IS NULL FROM background_jobs WHERE id='{job:D}'::uuid;"),
                "Provider outage retained a successful claim or unsafe diagnostic.");
            Require(await Scalar<bool>("SELECT scan_status='PENDING' AND version=1 AND sha256=@digest AND size_bytes=@size FROM attachments WHERE tenant_id=@tenant AND id=@file;"),
                "Provider outage changed quarantine integrity or visibility.");
            Require(await Scalar<long>("SELECT version FROM cards WHERE tenant_id=@tenant AND id=@card;") == revision,
                "Provider outage advanced the Card revision.");
            Require(await Scalar<long>($"SELECT (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant AND id='{job:D}'::uuid)+(SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND event_id='{job:D}'::uuid);") == 0,
                "Provider outage published a verdict event.");

            // Advance only this fixture's retry clock; never sleep through backoff.
            await Scalar<int>($"UPDATE background_jobs SET available_at=clock_timestamp()-interval '1 second' WHERE tenant_id=@tenant AND id='{job:D}'::uuid RETURNING 1;");
            var recoveredStorage = new FixtureStorage(new(tenant, file), bytes, fail: false);
            var recoveredScanner = new FixtureScanner(unavailable: false);
            await using (var connections = new PostgresConnectionFactory(workerConnection))
            {
                var processor = Processor(connections, recoveredStorage, recoveredScanner);
                Require(await processor.ProcessOneAsync(tenant, Guid.NewGuid(), ct) == JobProcessingResult.Completed,
                    "Recovered provider did not finish the original durable job.");
                Require(await processor.ProcessOneAsync(tenant, Guid.NewGuid(), ct) == JobProcessingResult.Empty,
                    "Recovered scan was dispatched again after acknowledgment.");
            }
            Require(recoveredStorage.Opens == 1 && recoveredScanner.Calls == 1,
                "Recovered scan repeated private provider I/O.");
            Require(await Scalar<bool>($"SELECT state='SUCCEEDED' AND attempt_count=2 AND lease_id IS NULL AND worker_id IS NULL FROM background_jobs WHERE id='{job:D}'::uuid;"),
                "Recovery lost the original job or its committed attempt count.");
            Require(await Scalar<string>($"SELECT safe_metadata::text FROM background_jobs WHERE id='{job:D}'::uuid;") == metadata,
                "Recovery replaced canonical scan metadata.");
            Require(await Scalar<bool>("SELECT scan_status='CLEAN' AND version=2 AND sha256=@digest AND size_bytes=@size FROM attachments WHERE tenant_id=@tenant AND id=@file;"),
                "Recovery lost source integrity or published multiple revisions.");
            Require(await Scalar<long>("SELECT version FROM cards WHERE tenant_id=@tenant AND id=@card;") == revision + 1,
                "Recovery did not advance the Card exactly once.");
            Require(await Scalar<long>($"SELECT (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant AND id='{job:D}'::uuid)+(SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND event_id='{job:D}'::uuid AND ready_at IS NOT NULL);") == 2,
                "Recovery did not publish exactly one audit and ready verdict event.");
        }
        Console.WriteLine("Attachment provider recovery: 2 bounded PostgreSQL-backed outage/retry cases passed; original source/job preserved and verdict published once.");
    }

    private static BackgroundJobProcessor Processor(PostgresConnectionFactory connections,
        IAttachmentObjectStorage storage, IAttachmentMalwareScanner scanner) =>
        new(new PostgresBackgroundJobStore(connections), new SystemClock(),
            [new AttachmentScanDeliveryHandler(new PostgresAttachmentScanDeliveryStore(connections), new(storage, scanner))]);

    private sealed class FixtureStorage(AttachmentObjectReference expected, byte[] bytes, bool fail) : IAttachmentObjectStorage
    {
        public int Opens { get; private set; }
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (reference != expected) throw new InvalidOperationException("Fixture source identity changed.");
            Opens++;
            if (fail) throw new IOException("PRIVATE-PROVIDER-DETAIL-MUST-NOT-BE-PERSISTED");
            return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        }
        public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximum, CancellationToken ct) =>
            throw new InvalidOperationException("Scan recovery cannot rewrite the source.");
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct) =>
            throw new InvalidOperationException("Scan recovery cannot delete the source.");
    }

    private sealed class FixtureScanner(bool unavailable) : IAttachmentMalwareScanner
    {
        public int Calls { get; private set; }
        public async Task<AttachmentScannerVerdict> ScanAsync(Stream source, CancellationToken ct)
        {
            Calls++;
            await source.CopyToAsync(Stream.Null, ct);
            return unavailable ? AttachmentScannerVerdict.Unavailable : AttachmentScannerVerdict.Clean;
        }
    }
}
