using System.Text;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Worker;

// Explicit release-image command: fixed public samples, no application startup,
// credentials, database or durable jobs. The daemon uses a test-only signature.
internal static class AttachmentScannerRuntimeVerification
{
    public static async Task<int> RunAsync()
    {
        try
        {
            var scanner = new ClamAvAttachmentMalwareScanner("/fixture/scanner.sock", TimeSpan.FromSeconds(5));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (!await scanner.IsReadyAsync(deadline.Token)) return Failed();
            // Multiple INSTREAM chunks exercise the actual engine, not a reply simulator.
            using var clean = new MemoryStream(Enumerable.Repeat((byte)'a', 150003).ToArray(), writable: false);
            using var detected = new MemoryStream(Encoding.ASCII.GetBytes("StrataAI real scanner rejection acceptance sample."), writable: false);
            using var empty = new MemoryStream();
            if (await scanner.ScanAsync(clean, deadline.Token) != AttachmentScannerVerdict.Clean
                || await scanner.ScanAsync(detected, deadline.Token) != AttachmentScannerVerdict.Infected
                || await scanner.ScanAsync(empty, deadline.Token) != AttachmentScannerVerdict.Unavailable
                || !clean.CanRead || !detected.CanRead || clean.Position != clean.Length || detected.Position != detected.Length)
                return Failed();
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            try { await scanner.ScanAsync(clean, cancelled.Token); return Failed(); }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
            clean.Position = 0;
            if (await scanner.ScanAsync(clean, deadline.Token) != AttachmentScannerVerdict.Clean) return Failed();
            Console.WriteLine("Worker real scanner transport verified: clean, test-signature detection, empty refusal, cancellation and recovery.");
            return 0;
        }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or OperationCanceledException or ArgumentException)
        { return Failed(); }
    }

    private static int Failed()
    {
        Console.WriteLine("Worker real scanner runtime verification failed.");
        return 1;
    }
}
