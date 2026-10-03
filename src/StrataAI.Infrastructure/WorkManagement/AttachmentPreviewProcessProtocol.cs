using System.Buffers.Binary;
using System.Security.Cryptography;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Bounded private pipe protocol. No filename, object key, tenant, URL, actor,
// provider configuration or diagnostic string is accepted/disclosed.
public static class AttachmentPreviewProcessProtocol
{
    private static ReadOnlySpan<byte> Magic => "SAPRV001"u8;
    private const int ResultHeaderSize = 22;
    private static readonly string[] FailureCodes = ["preview_decoder_unavailable", "preview_type_unsupported",
        "preview_source_unavailable", "preview_image_invalid", "preview_dimensions_exceeded", "preview_output_exceeded"];

    public static async Task WriteSourceAsync(Stream pipe, AttachmentScanRequest request, string mime, Stream source, CancellationToken ct)
    {
        var header = new byte[49]; Magic.CopyTo(header);
        header[8] = MimeCode(mime); BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(9), request.SizeBytes);
        Convert.FromHexString(request.Sha256).CopyTo(header, 17);
        var buffer = new byte[65536];
        try
        {
            if (!source.CanRead || !source.CanSeek || source.Length != request.SizeBytes)
                throw new AttachmentImagePreviewException("preview_source_unavailable");
            source.Position = 0; await pipe.WriteAsync(header, ct); long copied = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, request.SizeBytes - copied + 1)), ct);
                if (count == 0) break;
                if (count > request.SizeBytes - copied) throw new AttachmentImagePreviewException("preview_source_unavailable");
                hash.AppendData(buffer, 0, count); copied += count; await pipe.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (copied != request.SizeBytes || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), header.AsSpan(17, 32)))
                throw new AttachmentImagePreviewException("preview_source_unavailable");
            await pipe.FlushAsync(ct);
        }
        finally { CryptographicOperations.ZeroMemory(header); CryptographicOperations.ZeroMemory(buffer); }
    }

    public static async Task<int> RunChildAsync(Stream input, Stream output)
    {
        var stage = AttachmentPreviewFailureStage.Environment;
        try
        {
            if (!OperatingSystem.IsLinux()) throw new AttachmentImagePreviewException("preview_decoder_unavailable");
            var allowedEnvironment = new HashSet<string>(StringComparer.Ordinal)
            { "DOTNET_EnableDiagnostics", "DOTNET_GCHeapHardLimit", "DOTNET_GCRegionRange", "DOTNET_gcServer", "DOTNET_PROCESSOR_COUNT" };
            if (Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(key => !allowedEnvironment.Contains(key)))
                throw new AttachmentImagePreviewException("preview_decoder_unavailable");
            // Create an owned anonymous staging inode before filesystem
            // restriction. No request bytes have been read at this point.
            stage = AttachmentPreviewFailureStage.Scratch;
            var path = Path.Combine(Environment.CurrentDirectory, $"strata-preview-{Guid.NewGuid():N}.tmp");
            using var source = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None, BufferSize = 65536,
                Options = FileOptions.SequentialScan,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            File.Delete(path);
            stage = AttachmentPreviewFailureStage.Capabilities;
            LinuxAttachmentPreviewContainment.Apply();
            stage = AttachmentPreviewFailureStage.Source;
            var header = new byte[49]; await input.ReadExactlyAsync(header);
            if (!header.AsSpan(0, 8).SequenceEqual(Magic)) throw new AttachmentImagePreviewException("preview_image_invalid");
            var mime = header[8] switch { 1 => "image/png", 2 => "image/jpeg", 3 => "image/webp", _ => throw new AttachmentImagePreviewException("preview_type_unsupported") };
            var length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(9));
            if (length is < 1 or > 1073741824) throw new AttachmentImagePreviewException("preview_source_unavailable");
            var buffer = new byte[65536];
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long copied = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - copied + 1)));
                    if (count == 0) break;
                    if (count > length - copied) throw new AttachmentImagePreviewException("preview_source_unavailable");
                    copied += count; hash.AppendData(buffer, 0, count); await source.WriteAsync(buffer.AsMemory(0, count));
                }
                if (copied != length || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), header.AsSpan(17, 32)))
                    throw new AttachmentImagePreviewException("preview_source_unavailable");
                await source.FlushAsync(); source.Position = 0;
                stage = AttachmentPreviewFailureStage.RasterDecode;
                using var preview = new SkiaAttachmentImagePreviewDecoder(new()).Decode(source, mime, CancellationToken.None);
                var result = new byte[ResultHeaderSize]; Magic.CopyTo(result);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(10), preview.Width);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(14), preview.Height);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(18), checked((int)preview.SizeBytes));
                await output.WriteAsync(result); await preview.Bytes.CopyToAsync(output); await output.FlushAsync();
                return 0;
            }
            finally { CryptographicOperations.ZeroMemory(buffer); CryptographicOperations.ZeroMemory(header); }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var result = new byte[ResultHeaderSize]; Magic.CopyTo(result); result[8] = 1;
            result[9] = (byte)Math.Max(0, Array.IndexOf(FailureCodes, (error as AttachmentImagePreviewException)?.Code ?? "preview_decoder_unavailable"));
            result[10] = (byte)(error is AttachmentImagePreviewException { Stage: not AttachmentPreviewFailureStage.None } known ? known.Stage : stage);
            try { await output.WriteAsync(result); await output.FlushAsync(); } catch (IOException) { }
            return 1;
        }
    }

    public static async Task<AttachmentPreviewImage> ReadResultAsync(Stream pipe, CancellationToken ct)
    {
        var header = new byte[ResultHeaderSize]; await pipe.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 8).SequenceEqual(Magic)) throw new AttachmentImagePreviewException("preview_decoder_unavailable");
        if (header[8] == 1)
        {
            if (header[9] >= FailureCodes.Length || !Enum.IsDefined((AttachmentPreviewFailureStage)header[10]) || header.AsSpan(11).ContainsAnyExcept((byte)0))
                throw new AttachmentImagePreviewException("preview_decoder_unavailable");
            await RequireEndAsync(pipe, ct); throw new AttachmentImagePreviewException(FailureCodes[header[9]], (AttachmentPreviewFailureStage)header[10]);
        }
        var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10));
        var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(14));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(18));
        if (header[8] != 0 || header[9] != 0 || width is < 1 or > 1024 || height is < 1 or > 1024 || length is < 45 or > 8388608)
            throw new AttachmentImagePreviewException("preview_decoder_unavailable");
        var bytes = new byte[length]; var transferred = false;
        try
        {
            await pipe.ReadExactlyAsync(bytes, ct); await RequireEndAsync(pipe, ct);
            // Structural boundary validation stays managed in the parent; it
            // never invokes another native decoder on untrusted result bytes.
            if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
                || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8, 4)) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)
                || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) != width || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) != height
                || !bytes.AsSpan(length - 12).SequenceEqual(new byte[] {0,0,0,0,73,69,78,68,174,66,96,130}))
                throw new AttachmentImagePreviewException("preview_decoder_unavailable");
            var result = new AttachmentPreviewImage(bytes, width, height); transferred = true; return result;
        }
        finally { if (!transferred) CryptographicOperations.ZeroMemory(bytes); }
    }

    private static async Task RequireEndAsync(Stream pipe, CancellationToken ct)
    { if (await pipe.ReadAsync(new byte[1], ct) != 0) throw new AttachmentImagePreviewException("preview_decoder_unavailable"); }
    private static byte MimeCode(string mime) => mime switch
    { "image/png" => 1, "image/jpeg" => 2, "image/webp" => 3, _ => throw new AttachmentImagePreviewException("preview_type_unsupported") };
}
