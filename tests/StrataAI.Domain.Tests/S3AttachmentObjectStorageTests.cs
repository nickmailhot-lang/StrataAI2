using System.Net;
using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class S3AttachmentObjectStorageTests
{
    [Fact]
    public async Task Private_provider_reads_writes_and_cleanup_keep_equal_uuid_original_and_preview_objects_separate()
    {
        var ct=TestContext.Current.CancellationToken; using var client=new Client();
        var store=new S3AttachmentObjectStorage(client,Bucket,Owner);
        var tenant=Guid.NewGuid(); var id=Guid.NewGuid(); var original=new AttachmentObjectReference(tenant,id);
        var preview=AttachmentObjectReference.ForPreview(tenant,id);
        using(var bytes=new MemoryStream("original"u8.ToArray())) await store.WritePrivateAsync(original,bytes,bytes.Length,ct);
        using(var bytes=new MemoryStream("preview"u8.ToArray())) await store.WritePrivateAsync(preview,bytes,bytes.Length,ct);
        Assert.Equal(2,client.Objects.Count);
        async Task<byte[]> Read(AttachmentObjectReference reference)
        {
            await using var read=await store.OpenPrivateReadAsync(reference,ct); Assert.NotNull(read);
            using var copy=new MemoryStream(); await read.CopyToAsync(copy,ct); return copy.ToArray();
        }
        Assert.Equal("original"u8.ToArray(),await Read(original)); Assert.Equal("preview"u8.ToArray(),await Read(preview));
        Assert.Null(await store.OpenPrivateReadAsync(AttachmentObjectReference.ForPreview(Guid.NewGuid(),id),ct));
        Assert.True(await store.DeletePrivateAsync(preview,ct)); Assert.Null(await store.OpenPrivateReadAsync(preview,ct));
        Assert.Equal("original"u8.ToArray(),await Read(original)); Assert.Single(client.Objects);
    }
    private const string Bucket = "strataai-private-fixture", Owner = "123456789012";
    private sealed class Client(AmazonS3Config? configuration = null) : AmazonS3Client(new AnonymousAWSCredentials(), configuration ?? new AmazonS3Config { RegionEndpoint = RegionEndpoint.USEast1 })
    {
        public bool BlockPublic = true, PolicyPublic, OwnerEnforced = true;
        public string? MissingBlock;
        public string? Fail;
        public Action? AfterPart;
        public int Initiations, Aborts, Completions, Deletes;
        public readonly Dictionary<string, byte[]> Objects = new();
        private readonly Dictionary<string, List<byte[]>> _parts = new();
        public readonly List<int> PartSizes = [];
        public Stream? LastRead;
        public void Scope(string bucket, string owner, string? key = null)
        { Assert.Equal(Bucket, bucket); Assert.Equal(Owner, owner); if (key is not null) Assert.True(key.StartsWith("attachments/",StringComparison.Ordinal) || key.StartsWith("attachment-previews/",StringComparison.Ordinal)); }
        private void Fault(string step) { if (Fail == step) throw new AmazonS3Exception("private-provider-credential-and-path") { StatusCode = HttpStatusCode.ServiceUnavailable }; }
        public override Task<GetPublicAccessBlockResponse> GetPublicAccessBlockAsync(GetPublicAccessBlockRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Scope(request.BucketName, request.ExpectedBucketOwner); Fault("privacy");
            return Task.FromResult(new GetPublicAccessBlockResponse { PublicAccessBlockConfiguration = new()
            { BlockPublicAcls = MissingBlock != "acl", IgnorePublicAcls = MissingBlock != "ignore", BlockPublicPolicy = MissingBlock != "block-policy", RestrictPublicBuckets = BlockPublic } });
        }
        public override Task<GetBucketPolicyStatusResponse> GetBucketPolicyStatusAsync(GetBucketPolicyStatusRequest request, CancellationToken ct)
        { Scope(request.BucketName, request.ExpectedBucketOwner); return Task.FromResult(new GetBucketPolicyStatusResponse { PolicyStatus = new() { IsPublic = PolicyPublic } }); }
        public override Task<GetBucketOwnershipControlsResponse> GetBucketOwnershipControlsAsync(GetBucketOwnershipControlsRequest request, CancellationToken ct)
        { Scope(request.BucketName, request.ExpectedBucketOwner); return Task.FromResult(new GetBucketOwnershipControlsResponse { OwnershipControls = new() { Rules = [new() { ObjectOwnership = OwnerEnforced ? ObjectOwnership.BucketOwnerEnforced : ObjectOwnership.ObjectWriter }] } }); }
        public override Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(InitiateMultipartUploadRequest request, CancellationToken ct)
        {
            Scope(request.BucketName, request.ExpectedBucketOwner, request.Key); Fault("initiate"); Initiations++;
            Assert.Equal("application/octet-stream", request.ContentType); Assert.Equal(ServerSideEncryptionMethod.AES256, request.ServerSideEncryptionMethod);
            Assert.Null(request.CannedACL); Assert.Empty(request.Metadata.Keys);
            var upload = Guid.NewGuid().ToString("N"); _parts.Add(upload, []); return Task.FromResult(new InitiateMultipartUploadResponse { UploadId = upload });
        }
        public override async Task<UploadPartResponse> UploadPartAsync(UploadPartRequest request, CancellationToken ct)
        {
            Scope(request.BucketName, request.ExpectedBucketOwner, request.Key); Fault("part");
            using var read = new MemoryStream(); await request.InputStream.CopyToAsync(read, ct);
            Assert.Equal(read.Length, request.PartSize); Assert.InRange(read.Length, 1, 5 * 1024 * 1024);
            Assert.Equal(_parts[request.UploadId].Count + 1, request.PartNumber);
            _parts[request.UploadId].Add(read.ToArray()); PartSizes.Add((int)read.Length);
            AfterPart?.Invoke();
            return new() { ETag = $"\"part-{request.PartNumber}\"" };
        }
        public override Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(CompleteMultipartUploadRequest request, CancellationToken ct)
        {
            Scope(request.BucketName, request.ExpectedBucketOwner, request.Key); Assert.Equal("*", request.IfNoneMatch); Completions++;
            var bytes = _parts[request.UploadId].SelectMany(x => x).ToArray(); Assert.Equal(bytes.Length, request.MpuObjectSize);
            Assert.Equal(_parts[request.UploadId].Count, request.PartETags.Count);
            if (Objects.ContainsKey(request.Key)) throw new AmazonS3Exception("private existing key") { StatusCode = HttpStatusCode.PreconditionFailed };
            Objects.Add(request.Key, bytes);
            // Simulate a committed complete whose acknowledgement was lost.
            Fault("complete-after-commit"); _parts.Remove(request.UploadId); return Task.FromResult(new CompleteMultipartUploadResponse());
        }
        public override Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(AbortMultipartUploadRequest request, CancellationToken ct)
        { Scope(request.BucketName, request.ExpectedBucketOwner, request.Key); Aborts++; _parts.Remove(request.UploadId); Fault("abort"); return Task.FromResult(new AbortMultipartUploadResponse()); }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken ct)
        {
            Scope(request.BucketName, request.ExpectedBucketOwner, request.Key); Fault("read");
            if (!Objects.TryGetValue(request.Key, out var bytes)) throw Missing();
            LastRead = new MemoryStream(bytes, writable: false); return Task.FromResult(new GetObjectResponse { ResponseStream = LastRead });
        }
        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken ct)
        { Scope(request.BucketName, request.ExpectedBucketOwner, request.Key); if (!Objects.ContainsKey(request.Key)) throw Missing(); return Task.FromResult(new GetObjectMetadataResponse()); }
        public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken ct)
        { Scope(request.BucketName, request.ExpectedBucketOwner, request.Key); Deletes++; Objects.Remove(request.Key); return Task.FromResult(new DeleteObjectResponse()); }
        private static AmazonS3Exception Missing() => new("private absent key") { StatusCode = HttpStatusCode.NotFound, ErrorCode = "NoSuchKey" };
    }
    private sealed class NonSeekingSource(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
    private sealed class FaultingSource(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var count = await base.ReadAsync(buffer, ct); if (count == 0) throw new IOException("private-source-details"); return count; }
    }
    [Fact]
    public async Task ARCH_07_TC_01_Private_multipart_streams_measure_bytes_and_retain_scope_and_response_ownership()
    {
        var ct = TestContext.Current.CancellationToken; using var client = new Client(); var store = new S3AttachmentObjectStorage(client, Bucket, Owner);
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); var bytes = new byte[5 * 1024 * 1024 + 17];
        RandomNumberGenerator.Fill(bytes); using var input = new NonSeekingSource(bytes);
        var written = await store.WritePrivateAsync(reference, input, bytes.Length, ct);
        Assert.Equal(bytes.Length, written.SizeBytes); Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), written.Sha256);
        Assert.True(input.CanRead); Assert.Equal([5 * 1024 * 1024, 17], client.PartSizes); Assert.Equal(0, client.Aborts);
        await using (var read = await store.OpenPrivateReadAsync(reference, ct))
        { Assert.NotNull(read); Assert.False(read.CanSeek); Assert.False(read.CanWrite); using var copy = new MemoryStream(); await read.CopyToAsync(copy, ct); Assert.Equal(bytes, copy.ToArray()); }
        Assert.False(client.LastRead!.CanRead);
        var foreign = new AttachmentObjectReference(Guid.NewGuid(), reference.AttachmentId);
        Assert.Null(await store.OpenPrivateReadAsync(foreign, ct)); Assert.False(await store.DeletePrivateAsync(foreign, ct));
        Assert.True(await store.DeletePrivateAsync(reference, ct)); Assert.False(await store.DeletePrivateAsync(reference, ct)); Assert.Equal(1, client.Deletes);
    }
    [Fact]
    public async Task PRD_14_TC_07_Duplicate_identity_and_unknown_complete_never_overwrite_or_delete_committed_objects()
    {
        var ct = TestContext.Current.CancellationToken; using var client = new Client(); var store = new S3AttachmentObjectStorage(client, Bucket, Owner);
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); using var original = new MemoryStream([1, 2, 3]);
        await store.WritePrivateAsync(reference, original, 10, ct); using var duplicate = new MemoryStream([9, 9]);
        var exists = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(reference, duplicate, 10, ct));
        Assert.Equal("object_exists", exists.Code); Assert.Equal(new byte[] { 1, 2, 3 }, client.Objects[reference.ObjectKey]); Assert.Equal(1, client.Aborts);
        client.Fail = "complete-after-commit"; var ambiguous = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); using var input = new MemoryStream([7]);
        var failure = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(ambiguous, input, 10, ct));
        Assert.Equal("object_storage_unavailable", failure.Code); Assert.Equal(new byte[] { 7 }, client.Objects[ambiguous.ObjectKey]); Assert.Equal(0, client.Deletes);
        Assert.Equal(2, client.Aborts); Assert.DoesNotContain("private", failure.ToString());
    }
    [Theory]
    [InlineData(0, "object_empty")]
    [InlineData(11, "object_too_large")]
    public async Task PRD_14_TC_03_Invalid_actual_size_aborts_unpublished_parts(int size, string code)
    {
        using var client = new Client(); var store = new S3AttachmentObjectStorage(client, Bucket, Owner); using var input = new MemoryStream(new byte[size]);
        var failure = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(new(Guid.NewGuid(), Guid.NewGuid()), input, 10, TestContext.Current.CancellationToken));
        Assert.Equal(code, failure.Code); Assert.Equal(1, client.Aborts); Assert.Equal(0, client.Completions); Assert.Empty(client.Objects); Assert.True(input.CanRead);
    }
    [Theory]
    [InlineData("block")]
    [InlineData("policy")]
    [InlineData("ownership")]
    [InlineData("privacy")]
    [InlineData("acl")]
    [InlineData("ignore")]
    [InlineData("block-policy")]
    public async Task ARCH_07_TC_01_Unsafe_or_unverifiable_bucket_refuses_all_byte_operations(string unsafeSetting)
    {
        using var client = new Client(); client.BlockPublic = unsafeSetting != "block"; client.PolicyPublic = unsafeSetting == "policy";
        client.OwnerEnforced = unsafeSetting != "ownership"; client.Fail = unsafeSetting == "privacy" ? "privacy" : null;
        client.MissingBlock = unsafeSetting;
        var store = new S3AttachmentObjectStorage(client, Bucket, Owner); var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid());
        using var input = new MemoryStream([1]); var ct = TestContext.Current.CancellationToken;
        foreach (var operation in new Func<Task>[] { () => store.WritePrivateAsync(reference, input, 10, ct), () => store.OpenPrivateReadAsync(reference, ct), () => store.DeletePrivateAsync(reference, ct) })
        { var failure = await Assert.ThrowsAsync<AttachmentStorageException>(operation); Assert.Equal("object_storage_unavailable", failure.Code); Assert.DoesNotContain("private-provider", failure.ToString()); }
        Assert.Equal(0, client.Initiations); Assert.Equal(0, client.Deletes);
    }
    [Fact]
    public async Task ARCH_07_TC_01_Provider_part_failure_aborts_and_pre_cancelled_uploads_do_not_touch_provider()
    {
        using var client = new Client { Fail = "part" }; var store = new S3AttachmentObjectStorage(client, Bucket, Owner);
        using var input = new MemoryStream([1]); var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid());
        var failure = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(reference, input, 10, TestContext.Current.CancellationToken));
        Assert.Equal("object_storage_unavailable", failure.Code); Assert.Equal(1, client.Aborts); Assert.Empty(client.Objects);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WritePrivateAsync(reference, input, 10, cancelled.Token)); Assert.Equal(1, client.Initiations);
    }
    [Fact]
    public async Task ARCH_07_TC_01_Source_failure_and_mid_upload_cancellation_abort_parts_without_publishing()
    {
        var ct = TestContext.Current.CancellationToken; using var client = new Client(); var store = new S3AttachmentObjectStorage(client, Bucket, Owner);
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid());
        using var failedSource = new FaultingSource(new byte[5 * 1024 * 1024]);
        var failure = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(reference, failedSource, 10 * 1024 * 1024, ct));
        Assert.Equal("object_storage_unavailable", failure.Code); Assert.DoesNotContain("private-source", failure.ToString()); Assert.Equal(1, client.Aborts);
        Assert.Equal(0, client.Completions); Assert.Empty(client.Objects); Assert.True(failedSource.CanRead);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct); client.AfterPart = cancelled.Cancel;
        using var input = new MemoryStream(new byte[5 * 1024 * 1024]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WritePrivateAsync(reference, input, 10 * 1024 * 1024, cancelled.Token));
        Assert.Equal(2, client.Aborts); Assert.Equal(0, client.Completions); Assert.Empty(client.Objects); Assert.True(input.CanRead);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1073741825)]
    public async Task PRD_14_TC_03_Invalid_server_limits_do_not_consume_source_or_initiate_upload(long limit)
    {
        using var client = new Client(); var store = new S3AttachmentObjectStorage(client, Bucket, Owner); using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.WritePrivateAsync(new(Guid.NewGuid(), Guid.NewGuid()), input, limit, TestContext.Current.CancellationToken));
        Assert.Equal(0, input.Position); Assert.Equal(0, client.Initiations);
    }
    [Theory]
    [InlineData("../bucket", Owner)]
    [InlineData("bucket", "invalid-owner")]
    public void ARCH_07_TC_01_Runtime_scope_configuration_is_validated_without_echoing_input(string bucket, string owner)
    { using var client = new Client(); Assert.Throws<ArgumentException>(() => new S3AttachmentObjectStorage(client, bucket, owner)); }
    [Fact]
    public void ARCH_07_TC_01_Insecure_or_custom_provider_endpoints_are_refused()
    {
        using var insecure = new Client(new() { RegionEndpoint = RegionEndpoint.USEast1, UseHttp = true });
        Assert.Throws<ArgumentException>(() => new S3AttachmentObjectStorage(insecure, Bucket, Owner));
        using var custom = new Client(new() { ServiceURL = "https://untrusted.example.test", AuthenticationRegion = "us-east-1" });
        Assert.Throws<ArgumentException>(() => new S3AttachmentObjectStorage(custom, Bucket, Owner));
    }
}
