using System.Buffers.Binary;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Bounded signature/initial-header classification only. Full structural image
// decoding and malware inspection remain required before preview/publication.
public sealed class AttachmentFileTypeInspector : IAttachmentFileTypeInspector
{
    public AttachmentFileTypeProbe? InspectPrefix(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length is < 1 or > 256) return null;
        if (prefix.Length >= 33 && prefix[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && BinaryPrimitives.ReadUInt32BigEndian(prefix[8..12]) == 13 && prefix[12..16].SequenceEqual("IHDR"u8))
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(prefix[16..20]); var height = BinaryPrimitives.ReadUInt32BigEndian(prefix[20..24]);
            var depth = prefix[24]; var colour = prefix[25];
            var validDepth = colour switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 2 or 4 or 6 => depth is 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => false };
            if (width is > 0 and <= 32768 && height is > 0 and <= 32768 && (long)width * height <= 40000000
                && validDepth && prefix[26] == 0 && prefix[27] == 0 && prefix[28] <= 1) return new("image/png");
            return null;
        }
        if (prefix.Length >= 4 && prefix[0] == 0xff && prefix[1] == 0xd8 && prefix[2] == 0xff
            && (prefix[3] is >= 0xe0 and <= 0xef or 0xdb or 0xc0 or 0xc2)) return new("image/jpeg");
        if (prefix.Length >= 20 && prefix[..4].SequenceEqual("RIFF"u8) && prefix[8..12].SequenceEqual("WEBP"u8)
            && (prefix[12..16].SequenceEqual("VP8 "u8) || prefix[12..16].SequenceEqual("VP8L"u8) || prefix[12..16].SequenceEqual("VP8X"u8)))
        {
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(prefix[4..8]) + 8;
            return size >= 20 ? new("image/webp", size) : null;
        }
        if (prefix.Length >= 9 && prefix[..5].SequenceEqual("%PDF-"u8) && prefix[6] == (byte)'.'
            && (prefix[5] == (byte)'1' && prefix[7] is >= (byte)'0' and <= (byte)'7' || prefix[5] == (byte)'2' && prefix[7] == (byte)'0')
            && prefix[8] is 10 or 13) return new("application/pdf");
        return null;
    }
}
