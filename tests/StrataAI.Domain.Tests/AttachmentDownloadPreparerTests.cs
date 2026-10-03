using System.Security.Cryptography;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentDownloadPreparerTests
{
    private sealed class Source(byte[] bytes, bool failClose = false, Action? onRead = null) : MemoryStream(bytes, writable: false)
    {
        public bool Closed;
        public int MaximumRead;
        public override bool CanSeek => false;
        public override long Length => throw new InvalidOperationException("Source Length is untrusted.");
        public override long Position { get => throw new InvalidOperationException(); set => throw new InvalidOperationException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { MaximumRead = Math.Max(MaximumRead, buffer.Length); var read = await base.ReadAsync(buffer, ct); onRead?.Invoke(); return read; }
        protected override void Dispose(bool disposing)
        { Closed = true; base.Dispose(disposing); if (disposing && failClose) throw new IOException("Private close diagnostic."); }
    }
    private sealed class Objects(Source? source) : IAttachmentObjectStorage
    {
        public int Reads;
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Reads++; return Task.FromResult<Stream?>(source); }
        public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream input, long maximum, CancellationToken ct) => throw new InvalidOperationException("Download wrote an object.");
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct) => throw new InvalidOperationException("Download deleted an object.");
    }
    private static AttachmentScanRequest Claim(byte[] bytes) => new(new(Guid.NewGuid(), Guid.NewGuid()), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private static string DirectoryForTest() => Directory.CreateTempSubdirectory("strata-download-test-").FullName;

    [Theory]
    [InlineData(1)]
    [InlineData(65536)]
    [InlineData(131079)]
    public async Task Private_complete_verified_bytes_are_read_only_and_owned_until_disposal(int size)
    {
        if (!OperatingSystem.IsLinux()) return;
        var ct = TestContext.Current.CancellationToken; var root = DirectoryForTest();
        try
        {
            var bytes = new byte[size]; RandomNumberGenerator.Fill(bytes); var source = new Source(bytes);
            var provider = new Objects(source); var preparer = new PrivateAttachmentDownloadPreparer(provider, root);
            var prepared = await preparer.PrepareAsync(Claim(bytes), ct); Assert.NotNull(prepared);
            Assert.True(source.Closed); Assert.Equal(1, provider.Reads); Assert.InRange(source.MaximumRead, 1, 65536);
            Assert.True(prepared.CanRead); Assert.False(prepared.CanWrite); Assert.Equal(0, prepared.Position); Assert.Equal(size, prepared.Length);
            foreach (var path in Directory.GetFiles(root)) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            using var actual = new MemoryStream(); await prepared.CopyToAsync(actual, ct); Assert.Equal(bytes, actual.ToArray());
            Assert.Throws<NotSupportedException>(() => prepared.Write([1], 0, 1));
            await prepared.DisposeAsync(); await prepared.DisposeAsync(); Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root); }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task Short_long_and_corrupted_objects_never_return_delivery_bytes(int delta, bool corrupt)
    {
        if (!OperatingSystem.IsLinux()) return;
        var ct = TestContext.Current.CancellationToken; var root = DirectoryForTest();
        try
        {
            var expected = new byte[100003]; var actual = new byte[expected.Length + delta]; if (corrupt) actual[0] = 1;
            var source = new Source(actual); var preparer = new PrivateAttachmentDownloadPreparer(new Objects(source), root);
            var error = await Assert.ThrowsAsync<AttachmentStorageException>(() => preparer.PrepareAsync(Claim(expected), ct));
            Assert.Equal("object_storage_unavailable", error.Code); Assert.True(source.Closed); Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task Missing_object_does_not_create_a_staged_file()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = DirectoryForTest();
        try { Assert.Null(await new PrivateAttachmentDownloadPreparer(new Objects(null), root).PrepareAsync(Claim([1]), TestContext.Current.CancellationToken)); Assert.Empty(Directory.GetFiles(root)); }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task Close_failure_removes_staging_and_releases_slots_without_returning_content()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = DirectoryForTest(); var source = new Source([1], failClose: true);
        try
        {
            var preparer = new PrivateAttachmentDownloadPreparer(new Objects(source), root);
            await Assert.ThrowsAsync<IOException>(() => preparer.PrepareAsync(Claim([1]), TestContext.Current.CancellationToken));
            Assert.True(source.Closed); Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root); }
    }

    private sealed class FreshObjects : IAttachmentObjectStorage
    {
        public int Reads;
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
        { Reads++; return Task.FromResult<Stream?>(new MemoryStream([1], writable: false)); }
        public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximum, CancellationToken ct) => throw new InvalidOperationException();
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct) => throw new InvalidOperationException();
    }
    [Fact]
    public async Task Cancellation_during_read_disposes_the_private_source_and_partial_staging()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = DirectoryForTest(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = new Source(new byte[100003], onRead: cancel.Cancel);
        try
        {
            var preparer = new PrivateAttachmentDownloadPreparer(new Objects(source), root);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparer.PrepareAsync(Claim(new byte[100003]), cancel.Token));
            Assert.True(source.Closed); Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root); }
    }
    [Fact]
    public async Task Two_live_staged_files_bound_concurrency_and_cancelled_waiters_do_not_read_objects()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = DirectoryForTest(); var objects = new FreshObjects(); var preparer = new PrivateAttachmentDownloadPreparer(objects, root);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await using var first = (await preparer.PrepareAsync(Claim([1]), ct))!;
            await using var second = (await preparer.PrepareAsync(Claim([1]), ct))!;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var waiting = preparer.PrepareAsync(Claim([1]), cancel.Token); Assert.False(waiting.IsCompleted); Assert.Equal(2, objects.Reads);
            cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting); Assert.Equal(2, objects.Reads);
            await first.DisposeAsync();
            await using var replacement = (await preparer.PrepareAsync(Claim([1]), ct))!; Assert.Equal(3, objects.Reads);
        }
        finally { Assert.Empty(Directory.GetFiles(root)); Directory.Delete(root); }
    }
}
