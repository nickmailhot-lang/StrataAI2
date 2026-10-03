using System.Collections.Frozen;

namespace StrataAI.Application.WorkManagement;

// Server-owned classification, not a client MIME/filename or a clean verdict.
public sealed record AttachmentFileTypeProbe(string MimeType, long? DeclaredContainerSizeBytes = null);
public interface IAttachmentFileTypeInspector
{
    // Inspect at most the first 256 actual upload bytes. A recognized prefix
    // does not establish a valid complete document or safe image preview.
    AttachmentFileTypeProbe? InspectPrefix(ReadOnlySpan<byte> prefix);
}
public sealed class AttachmentUploadValidationException(string code) : Exception("Attachment file validation is unavailable.")
{
    public string Code { get; } = code is "attachment_type_not_allowed" or "attachment_too_large" or "attachment_integrity_invalid"
        ? code : throw new ArgumentException("A fixed attachment validation code is required.", nameof(code));
}
public sealed class AttachmentUploadPolicy
{
    private static readonly FrozenSet<string> Supported = new[] { "image/png", "image/jpeg", "image/webp", "application/pdf" }.ToFrozenSet(StringComparer.Ordinal);
    private readonly FrozenSet<string> _allowed;
    public AttachmentUploadPolicy(long maximumBytes, IEnumerable<string> allowedMimeTypes)
    {
        if (maximumBytes is < 1 or > 1073741824) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ArgumentNullException.ThrowIfNull(allowedMimeTypes);
        var values = allowedMimeTypes.Take(5).ToArray();
        if (values.Length is < 1 or > 4 || values.Any(value => value is null || !Supported.Contains(value))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("A nonempty canonical supported file-type allowlist is required.", nameof(allowedMimeTypes));
        MaximumBytes = maximumBytes; _allowed = values.ToFrozenSet(StringComparer.Ordinal);
    }
    public long MaximumBytes { get; }
    public IReadOnlySet<string> AllowedMimeTypes => _allowed;
    public AttachmentFileTypeProbe AdmitPrefix(IAttachmentFileTypeInspector inspector, ReadOnlySpan<byte> actualPrefix)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        if (actualPrefix.Length is < 1 or > 256) throw new AttachmentUploadValidationException("attachment_type_not_allowed");
        var probe = inspector.InspectPrefix(actualPrefix);
        if (probe is null || !_allowed.Contains(probe.MimeType)) throw new AttachmentUploadValidationException("attachment_type_not_allowed");
        if (probe.DeclaredContainerSizeBytes is <= 0) throw new AttachmentUploadValidationException("attachment_integrity_invalid");
        if (probe.DeclaredContainerSizeBytes > MaximumBytes) throw new AttachmentUploadValidationException("attachment_too_large");
        return probe;
    }
    // Invoke before metadata publication. On failure the durable upload intent
    // must reconcile the already-written private object; this method never
    // deletes an ambiguous provider result or grants publication/download.
    public void RequireMeasuredFile(AttachmentObjectReference intended, AttachmentFileTypeProbe probe, StoredAttachmentObject measured)
    {
        ArgumentNullException.ThrowIfNull(intended); ArgumentNullException.ThrowIfNull(probe); ArgumentNullException.ThrowIfNull(measured);
        if (!_allowed.Contains(probe.MimeType)) throw new AttachmentUploadValidationException("attachment_type_not_allowed");
        if (measured.SizeBytes > MaximumBytes) throw new AttachmentUploadValidationException("attachment_too_large");
        if (measured.Reference != intended || measured.SizeBytes <= 0 || measured.Sha256 is null || measured.Sha256.Length != 64
            || measured.Sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            || probe.DeclaredContainerSizeBytes is not null && probe.DeclaredContainerSizeBytes != measured.SizeBytes)
            throw new AttachmentUploadValidationException("attachment_integrity_invalid");
    }
}
