using System.Globalization;
using System.Text;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

// Runtime-derived enablement; Demo never enables managed file storage.
public sealed record AttachmentUploadAvailability(bool Enabled);

public static class AttachmentUploadTransport
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static PrepareAttachmentUploadInput? Read(HttpRequest request)
    {
        if (!string.Equals(request.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)) return null;
        string? Header(string name, int maximum) => request.Headers.TryGetValue(name, out var values)
            && values.Count == 1 && values[0] is { Length: > 0 } value && value.Length <= maximum ? value : null;
        var encoded = Header("X-Attachment-Name", 1360); var digest = Header("X-Attachment-SHA256", 64);
        if (encoded is null || digest is null || digest.Length != 64 || digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            || !long.TryParse(Header("X-Attachment-Size", 10), NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size is < 1 or > 1073741824
            || !long.TryParse(Header("X-Card-Version", 19), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version is < 1 or long.MaxValue
            || request.ContentLength is { } length && length != size) return null;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (Convert.ToBase64String(bytes) != encoded) return null;
            var name = StrictUtf8.GetString(bytes);
            if (name.Length is < 1 or > 255) return null;
            return new(name, size, digest, version);
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException) { return null; }
    }
}
