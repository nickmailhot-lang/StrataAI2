using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

public sealed class AttachmentImagePreviewPolicy(int maximumDimension = 32768, long maximumPixels = 40000000,
    int maximumOutputSide = 1024, int maximumOutputBytes = 8388608)
{
    public int MaximumDimension { get; } = maximumDimension is >= 1 and <= 32768 ? maximumDimension : throw new ArgumentOutOfRangeException(nameof(maximumDimension));
    public long MaximumPixels { get; } = maximumPixels is >= 1 and <= 40000000 ? maximumPixels : throw new ArgumentOutOfRangeException(nameof(maximumPixels));
    public int MaximumOutputSide { get; } = maximumOutputSide is >= 1 and <= 1024 ? maximumOutputSide : throw new ArgumentOutOfRangeException(nameof(maximumOutputSide));
    public int MaximumOutputBytes { get; } = maximumOutputBytes is >= 1 and <= 8388608 ? maximumOutputBytes : throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
}

public sealed class AttachmentImagePreviewException(string code) : Exception("Attachment image preview is unavailable.")
{
    public string Code { get; } = code is "preview_type_unsupported" or "preview_source_unavailable" or "preview_image_invalid"
        or "preview_dimensions_exceeded" or "preview_output_exceeded" or "preview_decoder_unavailable"
        ? code : throw new ArgumentException("A fixed image preview failure code is required.", nameof(code));
}

public interface IAttachmentImagePreviewDecoder
{
    // A lower-level transformation, never authorization or malware evidence.
    // Worker must supply owned, fully integrity-verified private source bytes
    // under its current durable-job capability. Source remains caller-owned.
    AttachmentPreviewImage Decode(Stream verifiedSource, string verifiedMimeType, CancellationToken ct);
}

public sealed class AttachmentPreviewImage : IDisposable
{
    private readonly byte[] _bytes;
    public AttachmentPreviewImage(byte[] encodedPng, int width, int height)
    {
        if (encodedPng is null || encodedPng.Length is < 1 or > 8388608 || width is < 1 or > 1024 || height is < 1 or > 1024)
            throw new ArgumentException("Bounded encoded preview bytes and dimensions are required.");
        _bytes = encodedPng; Width = width; Height = height; SizeBytes = encodedPng.Length;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(encodedPng)); Bytes = new MemoryStream(encodedPng, writable: false);
    }
    public int Width { get; }
    public int Height { get; }
    public string MimeType => "image/png";
    public long SizeBytes { get; }
    [JsonIgnore] public string Sha256 { get; }
    [JsonIgnore] public Stream Bytes { get; }
    public void Dispose() { Bytes.Dispose(); CryptographicOperations.ZeroMemory(_bytes); }
}
