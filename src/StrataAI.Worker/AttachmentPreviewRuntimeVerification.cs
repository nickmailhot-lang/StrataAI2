using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Worker;

// Fixed public fixture only. This command accepts no image, path, URL, tenant,
// policy override or provider input and exposes no HTTP route.
internal static class AttachmentPreviewRuntimeVerification
{
    public static AttachmentPreviewWorkerProcess CurrentExecutable()
    {
        var host = Environment.ProcessPath ?? "";
        if (Path.GetFileName(host) is not ("dotnet" or "dotnet.exe"))
            host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        return new(host, typeof(AttachmentPreviewRuntimeVerification).Assembly.Location);
    }

    public static async Task<int> RunAsync()
    {
        try
        {
            // A complete CRC-valid 1x1 RGBA red PNG, including IEND.
            using var source = new MemoryStream(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="), writable: false);
            var request = new AttachmentScanRequest(new(Guid.NewGuid(), Guid.NewGuid()), source.Length,
                Convert.ToHexStringLower(SHA256.HashData(source.ToArray())));
            var generator = new LinuxIsolatedAttachmentImagePreviewGenerator(CurrentExecutable());
            using var preview = await LinuxIsolatedAttachmentImagePreviewGenerator.VerifyPublicFixtureAsync(CurrentExecutable(), CancellationToken.None);
            if (preview.Width != 1 || preview.Height != 1 || preview.MimeType != "image/png"
                || preview.SizeBytes is < 1 or > 8388608 || !source.CanRead || preview.Bytes.CanWrite)
                return Failed();
            using var decoded = SKBitmap.Decode(preview.Bytes);
            if (decoded is null || decoded.Width != 1 || decoded.Height != 1 || decoded.GetPixel(0, 0) != SKColors.Red)
                return Failed();
            // A mismatched persisted measurement must never yield a preview;
            // child refusal must release the slot for a later correct request.
            try
            {
                using var invalid = await generator.GenerateAsync(new(request.Reference, request.SizeBytes, new string('0', 64)),
                    "image/png", source, CancellationToken.None);
                return Failed();
            }
            catch (AttachmentImagePreviewException) { }
            using var retry = await generator.GenerateAsync(request, "image/png", source, CancellationToken.None);
            if (retry.Width != 1 || retry.Height != 1 || !source.CanRead) return Failed();
            // Hold a fixed fixture's source read while the child waits for its
            // pipe payload. Cancellation must kill/reap the child and release
            // the singleton slot even when the parent runs as another uid.
            using var held = new HeldFixtureSource(source.Length);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancelled = generator.GenerateAsync(request, "image/png", held, cancellation.Token);
            await held.Waiting.Task.WaitAsync(cancellation.Token);
            await cancellation.CancelAsync();
            try { using var unexpected = await cancelled; return Failed(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            using var recovered = await generator.GenerateAsync(request, "image/png", source, CancellationToken.None);
            if (recovered.Width != 1 || recovered.Height != 1 || !source.CanRead) return Failed();
            Console.WriteLine("Worker isolated preview runtime verified using a fixed public PNG fixture, integrity refusal, cancellation and recovery.");
            return 0;
        }
        catch (AttachmentImagePreviewException error)
        {
            Console.Error.WriteLine($"Worker preview runtime verification failed at fixed stage {error.Stage} ({error.Code}).");
            return 1;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Native loader exceptions can include machine paths. CI needs only
            // a stable failure signal; no source, hash or exception diagnostics.
            return Failed();
        }
    }

    private sealed class HeldFixtureSource(long size) : Stream
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => size;
        public override long Position { get; set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            // Allow the fixed child to enter its ordinary pipe read before the
            // verifier cancels; no arbitrary provider/source bytes are passed.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            Waiting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static int Failed()
    {
        Console.Error.WriteLine("Worker preview runtime verification failed.");
        return 1;
    }
}
