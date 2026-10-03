using System.Security.Cryptography;
using System.Text;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentObjectStorageTests
{
    [Fact]
    public async Task Original_and_preview_with_the_same_uuid_survive_restart_and_cleanup_in_separate_private_namespaces()
    {
        var ct=TestContext.Current.CancellationToken; using var workspace=new Workspace();
        var tenant=Guid.NewGuid(); var id=Guid.NewGuid(); var original=new AttachmentObjectReference(tenant,id);
        var preview=AttachmentObjectReference.ForPreview(tenant,id);
        Assert.NotEqual(original,preview); Assert.Equal($"attachment-previews/{tenant:N}/{id:N}",preview.ObjectKey);
        var store=new LocalAttachmentObjectStorage(workspace.Root);
        using(var bytes=new MemoryStream("original"u8.ToArray())) await store.WritePrivateAsync(original,bytes,bytes.Length,ct);
        using(var bytes=new MemoryStream("preview"u8.ToArray())) await store.WritePrivateAsync(preview,bytes,bytes.Length,ct);
        var restarted=new LocalAttachmentObjectStorage(workspace.Root);
        async Task<byte[]> Read(AttachmentObjectReference reference)
        {
            await using var read=await restarted.OpenPrivateReadAsync(reference,ct); Assert.NotNull(read);
            using var copy=new MemoryStream(); await read.CopyToAsync(copy,ct); return copy.ToArray();
        }
        Assert.Equal("original"u8.ToArray(),await Read(original)); Assert.Equal("preview"u8.ToArray(),await Read(preview));
        Assert.Null(await restarted.OpenPrivateReadAsync(AttachmentObjectReference.ForPreview(Guid.NewGuid(),id),ct));
        Assert.True(await restarted.DeletePrivateAsync(preview,ct)); Assert.Null(await restarted.OpenPrivateReadAsync(preview,ct));
        Assert.Equal("original"u8.ToArray(),await Read(original));
        Assert.Throws<ArgumentException>(()=>AttachmentObjectReference.ForPreview(Guid.Empty,id));
        Assert.Throws<ArgumentException>(()=>AttachmentObjectReference.ForPreview(tenant,Guid.Empty));
    }
    private sealed class Workspace : IDisposable
    {
        private readonly string _parent = Path.GetFullPath(Path.GetTempPath());
        public string Root { get; }
        public Workspace() => Root = Path.Combine(_parent, $"strataai-attachment-storage-{Guid.NewGuid():N}");
        public void Dispose()
        {
            var full = Path.GetFullPath(Root); var relative = Path.GetRelativePath(_parent, full);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                || !relative.StartsWith("strataai-attachment-storage-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup scope.");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
    [Fact]
    public async Task Private_objects_measure_actual_stream_bytes_and_digest_survive_adapter_restart_and_keep_tenant_scope()
    {
        var ct = TestContext.Current.CancellationToken; using var workspace = new Workspace(); var store = new LocalAttachmentObjectStorage(workspace.Root);
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); var bytes = Encoding.UTF8.GetBytes("private binary fixture");
        using var input = new MemoryStream(bytes); var stored = await store.WritePrivateAsync(reference, input, bytes.Length, ct);
        Assert.Equal(bytes.Length, stored.SizeBytes); Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.Sha256);
        Assert.Equal($"attachments/{reference.OrganizationId:N}/{reference.AttachmentId:N}", stored.Reference.ObjectKey);
        Assert.True(input.CanRead); Assert.Equal(input.Length, input.Position);
        var restarted = new LocalAttachmentObjectStorage(workspace.Root);
        await using var read = await restarted.OpenPrivateReadAsync(reference, ct); Assert.NotNull(read);
        using var copy = new MemoryStream(); await read.CopyToAsync(copy, ct); Assert.Equal(bytes, copy.ToArray());
        Assert.Null(await restarted.OpenPrivateReadAsync(new(Guid.NewGuid(), reference.AttachmentId), ct));
        Assert.False(await restarted.DeletePrivateAsync(new(Guid.NewGuid(), reference.AttachmentId), ct));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(workspace.Root));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(workspace.Root, reference.ObjectKey)));
        }
    }
    [Fact]
    public async Task Concurrent_same_identity_never_overwrites_or_publishes_partial_objects_and_cleanup_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken; using var workspace = new Workspace(); var store = new LocalAttachmentObjectStorage(workspace.Root);
        for (var round = 0; round < 16; round++)
        {
            var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid());
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var arrivals = 0;
            async Task<StoredAttachmentObject?> Write(byte value)
            {
                using var input = new TogetherStream(Enumerable.Repeat(value, 100000).ToArray(), gate.Task,
                    () => { if (Interlocked.Increment(ref arrivals) == 8) gate.SetResult(); });
                // Distinct adapters sharing a root must coordinate through the
                // filesystem; an instance-local lock cannot satisfy this test.
                var writer = new LocalAttachmentObjectStorage(workspace.Root);
                try { return await writer.WritePrivateAsync(reference, input, 100000, ct); }
                catch (AttachmentStorageException failure) { Assert.Equal("object_exists", failure.Code); Assert.True(input.CanRead); return null; }
            }
            var outcomes = await Task.WhenAll(Enumerable.Range(1, 8).Select(value => Write((byte)value)));
            var winner = Assert.Single(outcomes, value => value is not null); Assert.NotNull(winner);
            await using (var read = await store.OpenPrivateReadAsync(reference, ct))
            {
                Assert.NotNull(read); using var bytes = new MemoryStream(); await read.CopyToAsync(bytes, ct);
                Assert.Equal(100000, bytes.Length); Assert.Single(bytes.ToArray().Distinct());
                Assert.Equal(winner.Sha256, Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant());
            }
            Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp", SearchOption.AllDirectories));
            Assert.True(await store.DeletePrivateAsync(reference, ct)); Assert.False(await store.DeletePrivateAsync(reference, ct));
            Assert.Null(await store.OpenPrivateReadAsync(reference, ct));
        }
    }
    private sealed class TogetherStream(byte[] bytes, Task gate, Action arrive) : MemoryStream(bytes)
    {
        private bool _arrived;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_arrived) { _arrived = true; arrive(); await gate.WaitAsync(ct); }
            return await base.ReadAsync(buffer, ct);
        }
    }
    [Theory]
    [InlineData(0, 10, "object_empty")]
    [InlineData(11, 10, "object_too_large")]
    [InlineData(65537, 65536, "object_too_large")]
    public async Task Empty_and_oversize_streams_leave_no_final_object_or_temporary_bytes(int size, long limit, string code)
    {
        var ct = TestContext.Current.CancellationToken; using var workspace = new Workspace(); var store = new LocalAttachmentObjectStorage(workspace.Root);
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); using var input = new MemoryStream(new byte[size]);
        var failure = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(reference, input, limit, ct)); Assert.Equal(code, failure.Code);
        Assert.Null(await store.OpenPrivateReadAsync(reference, ct)); Assert.Empty(Directory.GetFiles(workspace.Root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task Cancelled_or_failed_source_is_not_published_and_does_not_leak_provider_paths_in_failure()
    {
        var ct = TestContext.Current.CancellationToken; using var workspace = new Workspace(); var store = new LocalAttachmentObjectStorage(workspace.Root);
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var input = new MemoryStream(new byte[10]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WritePrivateAsync(reference, input, 10, cancellation.Token)); Assert.False(Directory.Exists(workspace.Root));
        using var broken = new BrokenSource(); var failure = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(reference, broken, 10, ct));
        Assert.Equal("object_storage_unavailable", failure.Code); Assert.DoesNotContain("private-source-error", failure.ToString()); Assert.DoesNotContain(workspace.Root, failure.ToString());
        Assert.Null(await store.OpenPrivateReadAsync(reference, ct)); Assert.Empty(Directory.GetFiles(workspace.Root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task Symbolic_links_are_rejected_for_root_ancestor_and_object_without_deleting_their_target()
    {
        var ct = TestContext.Current.CancellationToken; using var workspace = new Workspace(); using var outside = new Workspace();
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); var store = new LocalAttachmentObjectStorage(workspace.Root);
        using var input = new MemoryStream(new byte[10]); await store.WritePrivateAsync(reference, input, 10, ct);
        Directory.CreateDirectory(outside.Root); var secret = Path.Combine(outside.Root, "private-object"); await File.WriteAllTextAsync(secret, "protected target", ct);
        await store.DeletePrivateAsync(reference, ct);
        var objectPath = Path.Combine(workspace.Root, reference.ObjectKey);
        File.CreateSymbolicLink(objectPath, secret);
        var refused = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.OpenPrivateReadAsync(reference, ct)); Assert.Equal("object_path_unsafe", refused.Code);
        await Assert.ThrowsAsync<AttachmentStorageException>(() => store.DeletePrivateAsync(reference, ct));
        Assert.Equal("protected target", await File.ReadAllTextAsync(secret, ct)); File.Delete(objectPath);
        var link = Path.Combine(workspace.Root, "linked-root"); Directory.CreateSymbolicLink(link, outside.Root);
        Assert.Throws<AttachmentStorageException>(() => new LocalAttachmentObjectStorage(link)); Directory.Delete(link);
        var parent = Path.GetDirectoryName(objectPath)!; Directory.Delete(parent); Directory.CreateSymbolicLink(parent, outside.Root);
        await Assert.ThrowsAsync<AttachmentStorageException>(() => store.OpenPrivateReadAsync(reference, ct)); Directory.Delete(parent);
    }
    [Fact]
    public void Empty_scope_and_relative_roots_cannot_select_storage()
    {
        Assert.Throws<ArgumentException>(() => new AttachmentObjectReference(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new AttachmentObjectReference(Guid.NewGuid(), Guid.Empty));
        Assert.Throws<ArgumentException>(() => new LocalAttachmentObjectStorage("../private"));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1073741825)]
    public async Task Invalid_limits_are_refused_before_consuming_the_source_or_creating_a_directory(long maximum)
    {
        var ct = TestContext.Current.CancellationToken; using var workspace = new Workspace(); var store = new LocalAttachmentObjectStorage(workspace.Root);
        using var input = new MemoryStream(new byte[10]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.WritePrivateAsync(new(Guid.NewGuid(), Guid.NewGuid()), input, maximum, ct));
        Assert.Equal(0, input.Position); Assert.False(Directory.Exists(workspace.Root));
    }
    private sealed class BrokenSource : Stream
    {
        private bool _read;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => throw new IOException("private-source-error");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) throw new IOException("private-source-error");
            _read = true; buffer.Span[..5].Fill(42); return ValueTask.FromResult(5);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
