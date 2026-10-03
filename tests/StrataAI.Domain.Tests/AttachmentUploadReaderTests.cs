using System.Security.Cryptography;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentUploadReaderTests
{
    private sealed class Source(byte[] bytes, int chunk = 7) : MemoryStream(bytes)
    {
        public long ReadBytes;
        public Memory<byte>? PrefixBuffer { get; private set; }
        public bool Fail;
        public Action? AfterRead;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            PrefixBuffer ??= buffer;
            if (Fail) throw new IOException("private-source-path-and-details");
            var count = await base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], ct); ReadBytes += count; AfterRead?.Invoke(); return count;
        }
    }
    private static byte[] Jpeg(int size)
    { var bytes = Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray(); bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff; bytes[3] = 0xe0; return bytes; }
    [Theory]
    [InlineData(4, 1)]
    [InlineData(256, 7)]
    [InlineData(10003, 4096)]
    public async Task PRD_14_TC_01_Nonseeking_chunked_sources_replay_exact_prefix_once_and_preserve_full_digest(int size, int outputChunk)
    {
        var ct = TestContext.Current.CancellationToken; var expected = Jpeg(size); using var source = new Source(expected);
        var policy = new AttachmentUploadPolicy(size, ["image/jpeg"]);
        await using (var inspected = await AttachmentUploadReader.OpenAsync(source, policy, new AttachmentFileTypeInspector(), ct))
        {
            Assert.Equal("image/jpeg", inspected.Type.MimeType); Assert.Equal(Math.Min(size, 256), source.ReadBytes);
            Assert.False(inspected.Content.CanSeek); Assert.False(inspected.Content.CanWrite);
            Assert.Throws<NotSupportedException>(() => inspected.Content.Seek(0, SeekOrigin.Begin));
            using var copied = new MemoryStream(); var buffer = new byte[outputChunk]; int count;
            while ((count = await inspected.Content.ReadAsync(buffer.AsMemory(), ct)) != 0) await copied.WriteAsync(buffer.AsMemory(0, count), ct);
            Assert.Equal(expected, copied.ToArray()); Assert.Equal(size, source.ReadBytes);
            var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid());
            policy.RequireMeasuredFile(reference, inspected.Type, new(reference, copied.Length, Convert.ToHexString(SHA256.HashData(copied.ToArray())).ToLowerInvariant()));
            Assert.All(source.PrefixBuffer!.Value.ToArray(), value => Assert.Equal(0, value));
        }
        Assert.True(source.CanRead);
    }
    [Fact]
    public async Task PRD_14_TC_03_Unknown_and_oversize_prefixes_leave_source_owned_and_zero_buffer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var source = new Source(new byte[1000], 1000);
        var tooLarge = await Assert.ThrowsAsync<AttachmentUploadValidationException>(() => AttachmentUploadReader.OpenAsync(source, new(10, ["image/jpeg"]), new AttachmentFileTypeInspector(), ct));
        Assert.Equal("attachment_too_large", tooLarge.Code); Assert.Equal(11, source.ReadBytes); Assert.True(source.CanRead);
        Assert.All(source.PrefixBuffer!.Value.ToArray(), value => Assert.Equal(0, value));
        using var unknown = new Source("<svg>private</svg>"u8.ToArray());
        Assert.Equal("attachment_type_not_allowed", (await Assert.ThrowsAsync<AttachmentUploadValidationException>(() => AttachmentUploadReader.OpenAsync(unknown, new(1024, ["image/jpeg"]), new AttachmentFileTypeInspector(), ct))).Code);
        Assert.True(unknown.CanRead); Assert.All(unknown.PrefixBuffer!.Value.ToArray(), value => Assert.Equal(0, value));
    }
    [Fact]
    public async Task ARCH_07_TC_01_Faults_and_cancellation_retire_prefix_without_leaking_or_consuming_pre_cancelled_sources()
    {
        var ct = TestContext.Current.CancellationToken; var policy = new AttachmentUploadPolicy(1024, ["image/jpeg"]); var inspector = new AttachmentFileTypeInspector();
        using var failed = new Source(Jpeg(100)) { Fail = true };
        var failure = await Assert.ThrowsAsync<AttachmentUploadValidationException>(() => AttachmentUploadReader.OpenAsync(failed, policy, inspector, ct));
        Assert.Equal("attachment_source_unavailable", failure.Code); Assert.DoesNotContain("private-source", failure.ToString()); Assert.True(failed.CanRead);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct); using var source = new Source(Jpeg(100)); source.AfterRead = cancelled.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentUploadReader.OpenAsync(source, policy, inspector, cancelled.Token));
        Assert.All(source.PrefixBuffer!.Value.ToArray(), value => Assert.Equal(0, value)); Assert.True(source.CanRead);
        using var unopened = new Source(Jpeg(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentUploadReader.OpenAsync(unopened, policy, inspector, cancelled.Token)); Assert.Equal(0, unopened.ReadBytes);
    }
    [Fact]
    public async Task PRD_14_TC_06_Early_wrapper_disposal_zeroes_unreplayed_prefix_without_closing_original_source()
    {
        using var source = new Source(Jpeg(1000)); var upload = await AttachmentUploadReader.OpenAsync(source, new(1024, ["image/jpeg"]), new AttachmentFileTypeInspector(), TestContext.Current.CancellationToken);
        Assert.Equal(256, source.ReadBytes); await upload.DisposeAsync(); Assert.True(source.CanRead); Assert.False(upload.Content.CanRead);
        Assert.All(source.PrefixBuffer!.Value.ToArray(), value => Assert.Equal(0, value));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => upload.Content.ReadAsync(new byte[1].AsMemory(), TestContext.Current.CancellationToken).AsTask());
    }
}
