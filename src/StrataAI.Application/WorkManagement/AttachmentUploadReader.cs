using System.Security.Cryptography;

namespace StrataAI.Application.WorkManagement;

// Use only after upload authorization and durable original-intent admission.
// This reads a bounded prefix once, then replays it into private object storage
// without seeking, buffering the complete file or losing bytes from the digest.
public static class AttachmentUploadReader
{
    public static async Task<InspectedAttachmentUpload> OpenAsync(Stream source, AttachmentUploadPolicy policy,
        IAttachmentFileTypeInspector inspector, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(inspector);
        if (!source.CanRead) throw new ArgumentException("Attachment source must be readable.", nameof(source));
        ct.ThrowIfCancellationRequested(); var prefix = new byte[(int)Math.Min(256, policy.MaximumBytes + 1)]; var count = 0; var transferred = false;
        try
        {
            while (count < prefix.Length)
            {
                var read = await source.ReadAsync(prefix.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
            }
            ct.ThrowIfCancellationRequested();
            if (count > policy.MaximumBytes) throw new AttachmentUploadValidationException("attachment_too_large");
            var type = policy.AdmitPrefix(inspector, prefix.AsSpan(0, count));
            var result = new InspectedAttachmentUpload(type, new PrefixReplayStream(source, prefix, count)); transferred = true; return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AttachmentUploadValidationException) { throw; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { throw new AttachmentUploadValidationException("attachment_source_unavailable"); }
        finally { if (!transferred) CryptographicOperations.ZeroMemory(prefix); }
    }
    private sealed class PrefixReplayStream(Stream source, byte[] prefix, int length) : Stream
    {
        private int _position;
        private bool _disposed;
        public override bool CanRead => !_disposed && source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_position < length || buffer.Length == 0) return Replay(buffer);
            return source.Read(buffer);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); ct.ThrowIfCancellationRequested();
            if (_position < length || buffer.Length == 0) return ValueTask.FromResult(Replay(buffer.Span));
            return source.ReadAsync(buffer, ct);
        }
        private int Replay(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, length - _position);
            prefix.AsSpan(_position, count).CopyTo(buffer); _position += count;
            if (_position == length) CryptographicOperations.ZeroMemory(prefix);
            return count;
        }
        protected override void Dispose(bool disposing)
        { if (disposing && !_disposed) { _disposed = true; CryptographicOperations.ZeroMemory(prefix); } base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed class InspectedAttachmentUpload : IAsyncDisposable
{
    internal InspectedAttachmentUpload(AttachmentFileTypeProbe type, Stream content) { Type = type; Content = content; }
    public AttachmentFileTypeProbe Type { get; }
    // Caller still owns the original source. Retiring this wrapper zeroes its
    // buffered prefix, never closes the request's original stream.
    public Stream Content { get; }
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
