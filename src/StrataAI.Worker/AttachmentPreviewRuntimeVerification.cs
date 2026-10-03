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
            Console.WriteLine("Worker isolated preview runtime verified using a fixed public PNG fixture, integrity refusal and recovery.");
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

    private static int Failed()
    {
        Console.Error.WriteLine("Worker preview runtime verification failed.");
        return 1;
    }
}
