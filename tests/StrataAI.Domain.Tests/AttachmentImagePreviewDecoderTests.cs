using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentImagePreviewDecoderTests
{
    private sealed class Source(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool Closed;
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
    private static byte[] Fixture(SKEncodedImageFormat format, int width = 32, int height = 24)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, x < width / 2 ? y < height / 2 ? SKColors.Red : SKColors.Blue : y < height / 2 ? SKColors.Green : SKColors.Yellow);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(format, 95); return data.ToArray();
    }
    private static byte[] Read(Stream source) { using var bytes = new MemoryStream(); source.CopyTo(bytes); return bytes.ToArray(); }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public void Supported_actual_codecs_emit_only_bounded_fresh_PNG_and_retain_caller_source(SKEncodedImageFormat format, string mime)
    {
        using var source = new Source(Fixture(format));
        using var preview = new SkiaAttachmentImagePreviewDecoder(new(maximumOutputSide: 16)).Decode(source, mime, TestContext.Current.CancellationToken);
        Assert.False(source.Closed); Assert.Equal(16, preview.Width); Assert.Equal(12, preview.Height); Assert.Equal("image/png", preview.MimeType);
        Assert.False(preview.Bytes.CanWrite); var bytes = Read(preview.Bytes); Assert.Equal(bytes.Length, preview.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), preview.Sha256);
        using var parsed = SKBitmap.Decode(bytes); Assert.NotNull(parsed); Assert.Equal(preview.Width, parsed.Width); Assert.Equal(preview.Height, parsed.Height);
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data); Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.DoesNotContain(preview.Sha256, JsonSerializer.Serialize(preview), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bytes", JsonSerializer.Serialize(preview), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("image/jpeg", "preview_type_unsupported")]
    [InlineData("image/webp", "preview_type_unsupported")]
    [InlineData("application/pdf", "preview_type_unsupported")]
    [InlineData("image/svg+xml", "preview_type_unsupported")]
    public void Declared_type_does_not_override_actual_raster_format(string mime, string code)
    {
        using var source = new Source(Fixture(SKEncodedImageFormat.Png));
        var error = Assert.Throws<AttachmentImagePreviewException>(() => new SkiaAttachmentImagePreviewDecoder(new()).Decode(source, mime, TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Code); Assert.False(source.Closed);
    }

    [Fact]
    public void Fresh_encoding_discards_appended_private_metadata_and_payload()
    {
        const string marker = "PRIVATE-PREVIEW-SENTINEL<script>external payload</script>";
        var original = Fixture(SKEncodedImageFormat.Png).Concat(Encoding.UTF8.GetBytes(marker)).ToArray(); using var source = new Source(original);
        using var preview = new SkiaAttachmentImagePreviewDecoder(new()).Decode(source, "image/png", TestContext.Current.CancellationToken);
        var actual = Read(preview.Bytes); Assert.DoesNotContain(marker, Encoding.UTF8.GetString(actual), StringComparison.Ordinal);
        Assert.NotEqual(Convert.ToHexStringLower(SHA256.HashData(original)), preview.Sha256);
        using var decoded = SKBitmap.Decode(actual); Assert.NotNull(decoded); Assert.Equal(SKColors.Red, decoded.GetPixel(0, 0));
    }

    [Fact]
    public void Transparent_pixels_survive_normalization()
    {
        using var original = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul); original.SetPixel(0, 0, new(255, 0, 0, 128)); original.SetPixel(1, 0, SKColors.Transparent);
        using var image = SKImage.FromBitmap(original); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var source = new Source(encoded.ToArray()); using var preview = new SkiaAttachmentImagePreviewDecoder(new()).Decode(source, "image/png", TestContext.Current.CancellationToken);
        using var result = SKBitmap.Decode(Read(preview.Bytes)); Assert.Equal(128, result.GetPixel(0, 0).Alpha); Assert.Equal(0, result.GetPixel(1, 0).Alpha);
    }

    private static byte[] WithOrientation(byte[] jpeg, int orientation)
    {
        // Independent EXIF APP1/TIFF fixture: little endian, one SHORT entry.
        byte[] exif = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 18, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        return jpeg.Take(2).Concat(exif).Concat(jpeg.Skip(2)).ToArray();
    }
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 31, 0)]
    [InlineData(3, 31, 23)]
    [InlineData(4, 0, 23)]
    [InlineData(5, 0, 0)]
    [InlineData(6, 0, 23)]
    [InlineData(7, 31, 23)]
    [InlineData(8, 31, 0)]
    public void All_EXIF_origins_are_applied_to_pixels_and_output_dimensions(int orientation, int x, int y)
    {
        var bytes = WithOrientation(Fixture(SKEncodedImageFormat.Jpeg), orientation); using var source = new Source(bytes);
        using var rawData = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(rawData);
        Assert.Equal((SKEncodedOrigin)orientation, codec.EncodedOrigin);
        using var srgb = SKColorSpace.CreateSrgb(); var info = new SKImageInfo(32, 24, SKColorType.Rgba8888, SKAlphaType.Opaque, srgb);
        using var raw = new SKBitmap(info); Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, raw.GetPixels()));
        using var preview = new SkiaAttachmentImagePreviewDecoder(new()).Decode(source, "image/jpeg", TestContext.Current.CancellationToken);
        Assert.Equal(orientation >= 5 ? 24 : 32, preview.Width); Assert.Equal(orientation >= 5 ? 32 : 24, preview.Height);
        using var output = SKBitmap.Decode(Read(preview.Bytes)); Assert.Equal(raw.GetPixel(x, y), output.GetPixel(0, 0));
    }

    [Fact]
    public void Dimensions_and_pixel_count_are_refused_before_pixel_decode()
    {
        var bytes = Fixture(SKEncodedImageFormat.Png);
        foreach (var policy in new[] { new AttachmentImagePreviewPolicy(maximumDimension: 31), new AttachmentImagePreviewPolicy(maximumPixels: 767) })
        {
            using var source = new Source(bytes);
            Assert.Equal("preview_dimensions_exceeded", Assert.Throws<AttachmentImagePreviewException>(() => new SkiaAttachmentImagePreviewDecoder(policy).Decode(source, "image/png", TestContext.Current.CancellationToken)).Code);
        }
    }
    [Fact]
    public void Incomplete_pixels_and_output_size_failure_never_return_a_preview()
    {
        var bytes = Fixture(SKEncodedImageFormat.Png); using var shortSource = new Source(bytes.Take(bytes.Length / 2).ToArray());
        Assert.Equal("preview_image_invalid", Assert.Throws<AttachmentImagePreviewException>(() => new SkiaAttachmentImagePreviewDecoder(new()).Decode(shortSource, "image/png", TestContext.Current.CancellationToken)).Code);
        using var full = new Source(bytes);
        Assert.Equal("preview_output_exceeded", Assert.Throws<AttachmentImagePreviewException>(() => new SkiaAttachmentImagePreviewDecoder(new(maximumOutputBytes: 1)).Decode(full, "image/png", TestContext.Current.CancellationToken)).Code);
        Assert.False(shortSource.Closed); Assert.False(full.Closed);
    }

    private sealed class InterruptedSource(byte[] bytes, Action read, bool fail) : MemoryStream(bytes, writable: false)
    {
        public override int Read(Span<byte> buffer) { read(); if (fail) throw new IOException("Private source diagnostic."); return base.Read(buffer); }
    }
    [Fact]
    public void Native_callbacks_report_IO_at_managed_boundaries_and_cancellation_remains_cancellation()
    {
        var bytes = Fixture(SKEncodedImageFormat.Png); var decoder = new SkiaAttachmentImagePreviewDecoder(new());
        using var failed = new InterruptedSource(bytes, () => { }, fail: true);
        var error = Assert.Throws<AttachmentImagePreviewException>(() => decoder.Decode(failed, "image/png", TestContext.Current.CancellationToken));
        Assert.Equal("preview_source_unavailable", error.Code); Assert.Null(error.InnerException); Assert.DoesNotContain("Private", error.Message, StringComparison.Ordinal);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var interrupted = new InterruptedSource(bytes, cancel.Cancel, fail: false);
        Assert.ThrowsAny<OperationCanceledException>(() => decoder.Decode(interrupted, "image/png", cancel.Token));
        Assert.True(interrupted.CanRead);
    }
    [Fact]
    public void Policy_bounds_and_owned_PNG_cleanup_are_explicit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttachmentImagePreviewPolicy(maximumDimension: 32769));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttachmentImagePreviewPolicy(maximumPixels: 40000001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttachmentImagePreviewPolicy(maximumOutputSide: 1025));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttachmentImagePreviewPolicy(maximumOutputBytes: 8388609));
        var bytes = new byte[] { 1, 2, 3 }; var owned = new AttachmentPreviewImage(bytes, 1, 1); owned.Dispose(); owned.Dispose(); Assert.All(bytes, value => Assert.Equal(0, value));
    }
}
