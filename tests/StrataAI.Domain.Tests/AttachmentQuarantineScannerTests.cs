using System.Security.Cryptography;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentQuarantineScannerTests
{
    private static readonly byte[] Bytes = Enumerable.Range(0, 100003).Select(i => (byte)(i % 251)).ToArray();
    private static AttachmentScanRequest Request(long? size = null, string? hash = null) => new(
        new(Guid.NewGuid(), Guid.NewGuid()), size ?? Bytes.Length, hash ?? Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant());
    private sealed class Store(byte[]? bytes) : IAttachmentObjectStorage
    {
        public AttachmentObjectReference? Reference { get; private set; }
        public MemoryStream? Opened { get; private set; }
        public long? ClosedAt { get; private set; }
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Reference = reference; Opened = bytes is null ? null : new RecordingStream(bytes, position => ClosedAt = position); return Task.FromResult<Stream?>(Opened); }
        public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximumBytes, CancellationToken ct) => throw new InvalidOperationException("Scanning cannot write objects.");
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct) => throw new InvalidOperationException("Scanning cannot delete objects.");
    }
    private sealed class RecordingStream(byte[] bytes, Action<long> closed) : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing) { if (disposing && CanRead) closed(Position); base.Dispose(disposing); }
    }
    private sealed class Scanner(Func<Stream, CancellationToken, Task<AttachmentScannerVerdict>> run) : IAttachmentMalwareScanner
    {
        public int Calls { get; private set; }
        public Task<AttachmentScannerVerdict> ScanAsync(Stream source, CancellationToken ct) { Calls++; return run(source, ct); }
    }
    private static Scanner Complete(AttachmentScannerVerdict verdict, bool synchronous = false) => new(async (source, ct) =>
    {
        Assert.False(source.CanSeek); Assert.False(source.CanWrite);
        Assert.Throws<NotSupportedException>(() => source.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => source.WriteByte(1));
        var buffer = new byte[4096];
        if (synchronous) while (source.Read(buffer, 0, buffer.Length) != 0) { ct.ThrowIfCancellationRequested(); }
        else while (await source.ReadAsync(buffer.AsMemory(), ct) != 0) { }
        return verdict;
    });
    [Theory]
    [InlineData(AttachmentScannerVerdict.Clean, AttachmentScanStatus.Clean, false)]
    [InlineData(AttachmentScannerVerdict.Clean, AttachmentScanStatus.Clean, true)]
    [InlineData(AttachmentScannerVerdict.Infected, AttachmentScanStatus.Rejected, false)]
    public async Task PRD_14_TC_01_Complete_matching_bytes_bind_terminal_evidence_to_original_scope(AttachmentScannerVerdict verdict, AttachmentScanStatus status, bool synchronous)
    {
        var request = Request(); var store = new Store(Bytes); var scanner = Complete(verdict, synchronous);
        var result = await new AttachmentQuarantineScanner(store, scanner).ScanAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(status, result.Status); Assert.Same(request, result.Request); Assert.Same(request.Reference, store.Reference);
        Assert.Equal(status == AttachmentScanStatus.Clean ? "scan_clean" : "scan_rejected", result.Code);
        Assert.False(store.Opened!.CanRead); Assert.Equal(1, scanner.Calls);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100002)]
    public async Task PRD_14_TC_03_Early_clean_verdict_never_releases_partly_consumed_objects(int consumed)
    {
        var store = new Store(Bytes); var scanner = new Scanner(async (source, ct) =>
        { var buffer = new byte[consumed]; await source.ReadExactlyAsync(buffer, ct); return AttachmentScannerVerdict.Clean; });
        var result = await new AttachmentQuarantineScanner(store, scanner).ScanAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(AttachmentScanStatus.Failed, result.Status); Assert.Equal("scan_integrity_failed", result.Code); Assert.False(store.Opened!.CanRead);
    }
    [Fact]
    public async Task PRD_14_TC_03_Exact_length_scanner_must_also_match_digest_and_object_EOF()
    {
        var exact = new Scanner(async (source, ct) =>
        { var buffer = new byte[Bytes.Length]; await source.ReadExactlyAsync(buffer, ct); return AttachmentScannerVerdict.Clean; });
        var ct = TestContext.Current.CancellationToken;
        var good = await new AttachmentQuarantineScanner(new Store(Bytes), exact).ScanAsync(Request(), ct);
        Assert.Equal(AttachmentScanStatus.Clean, good.Status);
        var changed = Bytes.ToArray(); changed[123] ^= 1;
        var corrupt = await new AttachmentQuarantineScanner(new Store(changed), exact).ScanAsync(Request(), ct);
        Assert.Equal(AttachmentScanStatus.Failed, corrupt.Status); Assert.Equal("scan_integrity_failed", corrupt.Code);
        var extra = await new AttachmentQuarantineScanner(new Store([.. Bytes, 1, 2]), exact).ScanAsync(Request(), ct);
        Assert.Equal(AttachmentScanStatus.Failed, extra.Status); Assert.Equal("scan_integrity_failed", extra.Code);
        var shortObject = await new AttachmentQuarantineScanner(new Store(Bytes[..^1]), Complete(AttachmentScannerVerdict.Infected)).ScanAsync(Request(), ct);
        Assert.Equal(AttachmentScanStatus.Failed, shortObject.Status); Assert.Equal("scan_integrity_failed", shortObject.Code);
    }
    [Theory]
    [InlineData(AttachmentScannerVerdict.Unavailable)]
    [InlineData((AttachmentScannerVerdict)999)]
    public async Task ARCH_07_TC_01_Unavailable_and_unknown_provider_results_fail_closed(AttachmentScannerVerdict verdict)
    {
        var result = await new AttachmentQuarantineScanner(new Store(Bytes), Complete(verdict)).ScanAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(AttachmentScanStatus.Failed, result.Status); Assert.Equal("scan_provider_unavailable", result.Code);
    }
    [Fact]
    public async Task ARCH_07_TC_01_Missing_objects_provider_faults_and_disposed_streams_never_become_clean_or_echo_secrets()
    {
        var ct = TestContext.Current.CancellationToken; var scanner = Complete(AttachmentScannerVerdict.Clean);
        var missing = await new AttachmentQuarantineScanner(new Store(null), scanner).ScanAsync(Request(), ct);
        Assert.Equal("scan_object_missing", missing.Code); Assert.Equal(AttachmentScanStatus.Failed, missing.Status); Assert.Equal(0, scanner.Calls);
        var fault = new Scanner((_, _) => throw new IOException("private-provider-path-and-credential"));
        var failed = await new AttachmentQuarantineScanner(new Store(Bytes), fault).ScanAsync(Request(), ct);
        Assert.Equal("scan_provider_unavailable", failed.Code); Assert.Equal(AttachmentScanStatus.Failed, failed.Status);
        var disposed = new Scanner((source, _) => { source.Dispose(); return Task.FromResult(AttachmentScannerVerdict.Clean); });
        Assert.Equal(AttachmentScanStatus.Failed, (await new AttachmentQuarantineScanner(new Store(Bytes), disposed).ScanAsync(Request(), ct)).Status);
    }
    [Fact]
    public async Task ARCH_07_TC_01_Cancellation_propagates_and_retires_private_stream()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var store = new Store(Bytes); var scanner = new Scanner((_, _) => { cancellation.Cancel(); return Task.FromResult(AttachmentScannerVerdict.Clean); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AttachmentQuarantineScanner(store, scanner).ScanAsync(Request(), cancellation.Token));
        Assert.False(store.Opened!.CanRead);
        var unopened = new Store(Bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AttachmentQuarantineScanner(unopened, scanner).ScanAsync(Request(), cancellation.Token));
        Assert.Null(unopened.Reference);
    }
    [Fact]
    public async Task PRD_14_TC_03_Overlong_objects_expose_at_most_one_extra_byte_and_cannot_publish_a_verdict()
    {
        var store = new Store([.. Bytes, .. new byte[100000]]); var scanner = Complete(AttachmentScannerVerdict.Clean);
        var result = await new AttachmentQuarantineScanner(store, scanner).ScanAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(AttachmentScanStatus.Failed, result.Status);
        // The provider must never consume the remainder of an unexpectedly
        // oversized object. Capture its position before the owned stream closes.
        Assert.Equal(Bytes.Length + 1, store.ClosedAt);
    }
    [Fact]
    public async Task ARCH_07_TC_01_Provider_timeout_without_job_cancellation_is_a_failed_scan()
    {
        var scanner = new Scanner((_, _) => throw new OperationCanceledException("private scanner timeout"));
        var result = await new AttachmentQuarantineScanner(new Store(Bytes), scanner).ScanAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(AttachmentScanStatus.Failed, result.Status); Assert.Equal("scan_provider_unavailable", result.Code);
    }
    [Theory]
    [InlineData(0, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(1073741825, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, "invalid")]
    public void PRD_14_TC_03_Scan_requests_require_bounded_positive_size_and_canonical_digest(long size, string hash)
    { Assert.ThrowsAny<ArgumentException>(() => Request(size, hash)); }
}
