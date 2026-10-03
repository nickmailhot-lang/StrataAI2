using SkiaSharp;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Worker;

// Fixed public fixture only. This command accepts no image, path, URL, tenant,
// policy override or provider input and exposes no HTTP route.
internal static class AttachmentPreviewRuntimeVerification
{
    public static int Run()
    {
        try
        {
            // A complete CRC-valid 1x1 RGBA red PNG, including IEND.
            using var source = new MemoryStream(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="), writable: false);
            using var preview = new SkiaAttachmentImagePreviewDecoder(new()).Decode(source, "image/png", CancellationToken.None);
            if (preview.Width != 1 || preview.Height != 1 || preview.MimeType != "image/png"
                || preview.SizeBytes is < 1 or > 8388608 || !source.CanRead || preview.Bytes.CanWrite)
                return Failed();
            using var decoded = SKBitmap.Decode(preview.Bytes);
            if (decoded is null || decoded.Width != 1 || decoded.Height != 1 || decoded.GetPixel(0, 0) != SKColors.Red)
                return Failed();
            Console.WriteLine("Worker preview runtime verified using a fixed public PNG fixture.");
            return 0;
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
