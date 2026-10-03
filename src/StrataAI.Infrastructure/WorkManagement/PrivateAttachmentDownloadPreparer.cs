using System.Security.Cryptography;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Ephemeral staging for the adopted Linux release containers. Each temporary
// inode is created exclusively with mode 0600 and stays open until disposal;
// no filename/path is disclosed and no object is reopened by pathname.
public sealed class PrivateAttachmentDownloadPreparer(IAttachmentObjectStorage objects, string? temporaryDirectory = null)
    : IAttachmentDownloadPreparer
{
    private readonly SemaphoreSlim _slots = new(2, 2);
    public async Task<Stream?> PrepareAsync(AttachmentScanRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsLinux()) throw new AttachmentStorageException("object_storage_unavailable");
        await _slots.WaitAsync(ct);
        FileStream? prepared = null; Stream? source = null; var transferred = false; var buffer = new byte[65536];
        try
        {
            source = await objects.OpenPrivateReadAsync(request.Reference, ct);
            if (source is null) return null;
            var path = Path.Combine(temporaryDirectory ?? Path.GetTempPath(), $"strata-download-{Guid.NewGuid():N}.tmp");
            prepared = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None, BufferSize = 65536,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, request.SizeBytes - size + 1)), ct);
                if (read == 0) break;
                if (read > request.SizeBytes - size) throw new AttachmentStorageException("object_storage_unavailable");
                size += read; hash.AppendData(buffer, 0, read);
                await prepared.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            var actual = hash.GetHashAndReset(); var expected = Convert.FromHexString(request.Sha256);
            try
            {
                if (size != request.SizeBytes || !CryptographicOperations.FixedTimeEquals(actual, expected))
                    throw new AttachmentStorageException("object_storage_unavailable");
            }
            finally { CryptographicOperations.ZeroMemory(actual); CryptographicOperations.ZeroMemory(expected); }
            await prepared.FlushAsync(ct); prepared.Position = 0; ct.ThrowIfCancellationRequested();
            await source.DisposeAsync(); source = null;
            var result = new Lease(prepared, _slots); transferred = true; return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            try { if (source is not null) await source.DisposeAsync(); }
            finally
            {
                if (!transferred)
                {
                    try { if (prepared is not null) await prepared.DisposeAsync(); }
                    finally { _slots.Release(); }
                }
            }
        }
    }

    private sealed class Lease(FileStream file, SemaphoreSlim slots) : Stream
    {
        private int _closed;
        public override bool CanRead => _closed == 0 && file.CanRead;
        public override bool CanSeek => _closed == 0 && file.CanSeek;
        public override bool CanWrite => false;
        public override long Length => file.Length;
        public override long Position { get => file.Position; set => file.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => file.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => file.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _closed, 1) == 0)
                try { file.Dispose(); } finally { slots.Release(); }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
                try { await file.DisposeAsync(); } finally { slots.Release(); }
            GC.SuppressFinalize(this);
        }
    }
}
