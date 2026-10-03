using SkiaSharp;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// CPU raster processing for the separate Worker. No URL/path/provider read,
// font loading, vector/PDF rendering or persistence is available here.
public sealed class SkiaAttachmentImagePreviewDecoder(AttachmentImagePreviewPolicy policy) : IAttachmentImagePreviewDecoder
{
    public AttachmentPreviewImage Decode(Stream verifiedSource, string verifiedMimeType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(verifiedSource); ct.ThrowIfCancellationRequested();
        var expectedFormat = verifiedMimeType switch
        {
            "image/png" => SKEncodedImageFormat.Png, "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/webp" => SKEncodedImageFormat.Webp, _ => throw new AttachmentImagePreviewException("preview_type_unsupported")
        };
        try
        {
            if (!verifiedSource.CanRead || !verifiedSource.CanSeek || verifiedSource.Length is < 1 or > 1073741824)
                throw new AttachmentImagePreviewException("preview_source_unavailable");
            verifiedSource.Position = 0;
            using var input = new SafeInput(verifiedSource, verifiedSource.Length, ct);
            using var managed = new SKManagedStream(input, disposeManagedStream: false);
            using var codec = SKCodec.Create(managed, out var created);
            ct.ThrowIfCancellationRequested();
            if (input.Failed) throw new AttachmentImagePreviewException("preview_source_unavailable");
            if (codec is null || created != SKCodecResult.Success) throw new AttachmentImagePreviewException("preview_image_invalid");
            if (codec.EncodedFormat != expectedFormat) throw new AttachmentImagePreviewException("preview_type_unsupported");
            var original = codec.Info;
            if (original.Width < 1 || original.Height < 1 || original.Width > policy.MaximumDimension || original.Height > policy.MaximumDimension
                || (long)original.Width * original.Height > policy.MaximumPixels)
                throw new AttachmentImagePreviewException("preview_dimensions_exceeded");
            using var srgb = SKColorSpace.CreateSrgb();
            var pixels = new SKImageInfo(original.Width, original.Height, SKColorType.Rgba8888,
                original.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul, srgb);
            using var bitmap = new SKBitmap(pixels);
            if (bitmap.GetPixels() == IntPtr.Zero) throw new AttachmentImagePreviewException("preview_decoder_unavailable");
            try
            {
                var decoded = codec.GetPixels(pixels, bitmap.GetPixels());
                ct.ThrowIfCancellationRequested();
                if (input.Failed) throw new AttachmentImagePreviewException("preview_source_unavailable");
                // Partial pixels are not a successful preview, even if a
                // library's convenience image decode would display them.
                if (decoded != SKCodecResult.Success) throw new AttachmentImagePreviewException("preview_image_invalid");
                var origin = codec.EncodedOrigin;
                var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
                var orientedWidth = swapped ? original.Height : original.Width;
                var orientedHeight = swapped ? original.Width : original.Height;
                var scale = Math.Min(1d, (double)policy.MaximumOutputSide / Math.Max(orientedWidth, orientedHeight));
                var width = Math.Max(1, (int)Math.Round(orientedWidth * scale));
                var height = Math.Max(1, (int)Math.Round(orientedHeight * scale));
                using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
                if (surface is null) throw new AttachmentImagePreviewException("preview_decoder_unavailable");
                try
                {
                    var canvas = surface.Canvas; canvas.Clear(SKColors.Transparent);
                    canvas.Scale((float)width / orientedWidth, (float)height / orientedHeight);
                    Orient(canvas, origin, original.Width, original.Height);
                    using var image = SKImage.FromBitmap(bitmap);
                    canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
                    ct.ThrowIfCancellationRequested();
                    using var rendered = surface.Snapshot(); using var png = rendered.Encode(SKEncodedImageFormat.Png, 100);
                    ct.ThrowIfCancellationRequested();
                    if (png is null || png.Size < 1 || png.Size > policy.MaximumOutputBytes)
                        throw new AttachmentImagePreviewException("preview_output_exceeded");
                    return new(png.ToArray(), width, height);
                }
                finally { surface.Canvas.Clear(SKColors.Transparent); }
            }
            finally { bitmap.Erase(SKColors.Transparent); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or NotSupportedException)
        { throw new AttachmentImagePreviewException("preview_source_unavailable"); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException)
        { throw new AttachmentImagePreviewException("preview_decoder_unavailable"); }
    }

    private static void Orient(SKCanvas canvas, SKEncodedOrigin origin, int width, int height)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopLeft: break;
            case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(270); break;
            default: throw new AttachmentImagePreviewException("preview_image_invalid");
        }
    }

    // A native stream callback must not throw managed I/O/cancellation through
    // native codec frames. Return EOF on interruption and report the failure
    // at a managed stage boundary, without disposing the caller's source.
    private sealed class SafeInput(Stream source, long length, CancellationToken ct) : Stream
    {
        public bool Failed { get; private set; }
        public override bool CanRead => !Failed && !ct.IsCancellationRequested && source.CanRead;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get { try { return Failed ? 0 : source.Position; } catch (Exception error) when (IoError(error)) { Failed = true; return 0; } }
            set { try { if (!ct.IsCancellationRequested && !Failed) { if (value < 0 || value > length) { Failed = true; return; } source.Position = value; } } catch (Exception error) when (IoError(error)) { Failed = true; } }
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (!CanRead) return 0;
            try { var remaining = length - source.Position; return remaining <= 0 ? 0 : source.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]); }
            catch (Exception error) when (IoError(error)) { Failed = true; return 0; }
        }
        public override long Seek(long offset, SeekOrigin origin)
        { try { if (!CanRead) return -1; return source.Seek(offset, origin); } catch (Exception error) when (IoError(error)) { Failed = true; return -1; } }
        private static bool IoError(Exception error) => error is IOException or UnauthorizedAccessException or ObjectDisposedException or NotSupportedException or ArgumentException or OverflowException;
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
