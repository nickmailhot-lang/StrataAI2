using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Managed AWS S3 adapter, explicitly constructed from runtime credentials. This
// type never provisions a bucket or falls back to a local/Demo store.
public sealed class S3AttachmentObjectStorage : IAttachmentObjectStorage
{
    private const int PartSize = 5 * 1024 * 1024;
    private readonly IAmazonS3 _client;
    private readonly string _bucket, _owner;
    public S3AttachmentObjectStorage(IAmazonS3 client, string bucket, string expectedBucketOwner)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateBucketConfiguration(bucket, expectedBucketOwner);
        if (client.Config.UseHttp || !string.IsNullOrEmpty(client.Config.ServiceURL))
            throw new ArgumentException("Managed attachment storage requires the regional HTTPS AWS endpoint.", nameof(client));
        _client = client; _bucket = bucket; _owner = expectedBucketOwner;
    }
    internal static void ValidateBucketConfiguration(string bucket, string expectedBucketOwner)
    {
        if (string.IsNullOrEmpty(bucket) || bucket.Length is < 3 or > 63
            || bucket.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
            || !char.IsAsciiLetterOrDigit(bucket[0]) || !char.IsAsciiLetterOrDigit(bucket[^1]))
            throw new ArgumentException("A canonical managed storage bucket is required.", nameof(bucket));
        if (expectedBucketOwner is null || expectedBucketOwner.Length != 12 || expectedBucketOwner.Any(c => c is not (>= '0' and <= '9')))
            throw new ArgumentException("An expected managed storage owner is required.", nameof(expectedBucketOwner));
    }
    public async Task ValidatePrivateBucketAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var access = await _client.GetPublicAccessBlockAsync(new() { BucketName = _bucket, ExpectedBucketOwner = _owner }, ct);
            var block = access.PublicAccessBlockConfiguration;
            if (block is null || block.BlockPublicAcls != true || block.IgnorePublicAcls != true
                || block.BlockPublicPolicy != true || block.RestrictPublicBuckets != true) throw Unavailable();
            var policy = await _client.GetBucketPolicyStatusAsync(new() { BucketName = _bucket, ExpectedBucketOwner = _owner }, ct);
            if (policy.PolicyStatus?.IsPublic != false) throw Unavailable();
            var ownership = await _client.GetBucketOwnershipControlsAsync(new() { BucketName = _bucket, ExpectedBucketOwner = _owner }, ct);
            if (ownership.OwnershipControls?.Rules is not { Count: 1 } rules || rules[0].ObjectOwnership != ObjectOwnership.BucketOwnerEnforced)
                throw Unavailable();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (IsProviderFailure(error)) { throw Unavailable(); }
    }
    public async Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximumBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Attachment source must be readable.", nameof(source));
        if (maximumBytes is < 1 or > 1073741824) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ct.ThrowIfCancellationRequested(); string? uploadId = null;
        try
        {
            await ValidatePrivateBucketAsync(ct);
            var initiated = await _client.InitiateMultipartUploadAsync(new()
            {
                BucketName = _bucket, ExpectedBucketOwner = _owner, Key = reference.ObjectKey,
                ContentType = "application/octet-stream", ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
                // No ACL or caller filename/MIME is sent to the provider.
            }, ct);
            if (string.IsNullOrEmpty(initiated.UploadId)) throw Unavailable();
            uploadId = initiated.UploadId; var parts = new List<PartETag>();
            var buffer = new byte[PartSize]; long size = 0; var eof = false;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (!eof)
            {
                var filled = 0;
                while (filled < buffer.Length)
                {
                    var count = await source.ReadAsync(buffer.AsMemory(filled, (int)Math.Min(buffer.Length - filled, maximumBytes - size + 1)), ct);
                    if (count == 0) { eof = true; break; }
                    if (count > maximumBytes - size) throw new AttachmentStorageException("object_too_large");
                    hash.AppendData(buffer, filled, count); filled += count; size += count;
                }
                if (filled == 0) continue;
                using var part = new MemoryStream(buffer, 0, filled, writable: false);
                var number = parts.Count + 1;
                var uploaded = await _client.UploadPartAsync(new()
                {
                    BucketName = _bucket, ExpectedBucketOwner = _owner, Key = reference.ObjectKey, UploadId = uploadId,
                    PartNumber = number, PartSize = filled, InputStream = part
                }, ct);
                if (string.IsNullOrEmpty(uploaded.ETag)) throw Unavailable();
                parts.Add(new(number, uploaded.ETag));
            }
            if (size == 0) throw new AttachmentStorageException("object_empty");
            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            ct.ThrowIfCancellationRequested();
            await _client.CompleteMultipartUploadAsync(new()
            {
                BucketName = _bucket, ExpectedBucketOwner = _owner, Key = reference.ObjectKey,
                UploadId = uploadId, PartETags = parts, IfNoneMatch = "*", MpuObjectSize = size
            }, ct);
            uploadId = null; return new(reference, size, digest);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.PreconditionFailed)
        { throw new AttachmentStorageException("object_exists"); }
        catch (Exception error) when (IsProviderFailure(error)) { throw Unavailable(); }
        finally
        {
            if (uploadId is not null)
            {
                // Abort removes only unpublished parts. Completion timeouts may
                // already have committed; never compensate by deleting the key.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await _client.AbortMultipartUploadAsync(new() { BucketName = _bucket, ExpectedBucketOwner = _owner,
                    Key = reference.ObjectKey, UploadId = uploadId }, cleanup.Token).WaitAsync(cleanup.Token); }
                catch (Exception error) when (IsProviderFailure(error)) { }
            }
        }
    }
    public async Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference); ct.ThrowIfCancellationRequested();
        try
        {
            await ValidatePrivateBucketAsync(ct);
            var response = await _client.GetObjectAsync(new() { BucketName = _bucket, ExpectedBucketOwner = _owner, Key = reference.ObjectKey }, ct);
            if (response.ResponseStream is null) { response.Dispose(); throw Unavailable(); }
            return new OwnedResponseStream(response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound && error.ErrorCode is "NoSuchKey" or "NotFound") { return null; }
        catch (Exception error) when (IsProviderFailure(error)) { throw Unavailable(); }
    }
    public async Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference); ct.ThrowIfCancellationRequested();
        try
        {
            await ValidatePrivateBucketAsync(ct);
            await _client.GetObjectMetadataAsync(new() { BucketName = _bucket, ExpectedBucketOwner = _owner, Key = reference.ObjectKey }, ct);
            await _client.DeleteObjectAsync(new() { BucketName = _bucket, ExpectedBucketOwner = _owner, Key = reference.ObjectKey }, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound && error.ErrorCode is "NoSuchKey" or "NotFound") { return false; }
        catch (Exception error) when (IsProviderFailure(error)) { throw Unavailable(); }
    }
    private static bool IsProviderFailure(Exception error) => error is Amazon.Runtime.AmazonServiceException or Amazon.Runtime.AmazonClientException or IOException or HttpRequestException or OperationCanceledException;
    private static AttachmentStorageException Unavailable() => new("object_storage_unavailable");
    private sealed class OwnedResponseStream(GetObjectResponse response) : Stream
    {
        private Stream Source => response.ResponseStream;
        public override bool CanRead => Source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Source.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => Source.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Source.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Source.ReadAsync(buffer, ct);
        protected override void Dispose(bool disposing) { if (disposing) response.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
