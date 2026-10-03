using System.Globalization;
using System.Text.RegularExpressions;
using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

public enum AttachmentKind { File, Url }
public enum AttachmentScanStatus { NotApplicable, Pending, Clean, Rejected, Failed }

// Storage keys and verified MIME/size are server-owned inputs. HTTP upload DTOs
// must not bind directly to this factory or treat client metadata as verified.
public sealed class Attachment : DomainEntity, IOrganizationScoped
{
    private Attachment(Guid id, Guid organizationId, Guid cardId, Guid uploaderId, string displayName,
        AttachmentKind kind, string? mimeType, long? sizeBytes, string? storageKey, string? sha256, string? url, DateTimeOffset at)
        : base(id, at.ToUniversalTime())
    {
        if (organizationId == Guid.Empty || cardId == Guid.Empty || uploaderId == Guid.Empty)
            throw new ArgumentException("Attachment scope and uploader are required.");
        OrganizationId = organizationId; CardId = cardId; UploaderId = uploaderId;
        DisplayName = RequireDisplayName(displayName); Kind = kind; MimeType = mimeType;
        SizeBytes = sizeBytes; StorageKey = storageKey; Sha256 = sha256; Url = url;
        ScanStatus = kind == AttachmentKind.File ? AttachmentScanStatus.Pending : AttachmentScanStatus.NotApplicable;
    }
    public Guid OrganizationId { get; }
    public Guid CardId { get; }
    public Guid UploaderId { get; }
    public AttachmentKind Kind { get; }
    public string DisplayName { get; }
    public string? MimeType { get; }
    public long? SizeBytes { get; }
    public string? StorageKey { get; }
    public string? Sha256 { get; }
    public string? Url { get; }
    public AttachmentScanStatus ScanStatus { get; private set; }
    public DateTimeOffset? ScannedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool CanDownload => Kind == AttachmentKind.File && Sha256 is not null && DeletedAt is null && ScanStatus == AttachmentScanStatus.Clean;
    public bool CanPreviewImage => CanDownload && MimeType is ("image/png" or "image/jpeg" or "image/webp");
    public bool CanUseAsCoverFor(Guid organizationId, Guid cardId) => CanPreviewImage && OrganizationId == organizationId && CardId == cardId;

    public static Attachment QuarantineFile(Guid id, Guid organizationId, Guid cardId, Guid uploaderId, string displayName,
        string verifiedMimeType, long verifiedSizeBytes, string serverStorageKey, string verifiedSha256, DateTimeOffset at)
    {
        var mime = verifiedMimeType?.Trim().ToLowerInvariant();
        if (mime is null || mime.Length > 127 || !Regex.IsMatch(mime, @"\A[a-z0-9!#$&^_.+\-]+/[a-z0-9!#$&^_.+\-]+\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Verified MIME type is invalid.", nameof(verifiedMimeType));
        if (verifiedSizeBytes is <= 0 or > 1073741824) throw new ArgumentOutOfRangeException(nameof(verifiedSizeBytes), "Verified file size must fit the storage bound.");
        if (verifiedSha256 is null || verifiedSha256.Length != 64
            || verifiedSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Verified file digest is invalid.", nameof(verifiedSha256));
        if (string.IsNullOrWhiteSpace(serverStorageKey) || serverStorageKey.Length > 512
            || serverStorageKey.Any(char.IsControl) || serverStorageKey.Contains('\\') || serverStorageKey.StartsWith('/')
            || serverStorageKey.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("Server storage key is invalid.", nameof(serverStorageKey));
        return new(id, organizationId, cardId, uploaderId, displayName, AttachmentKind.File, mime, verifiedSizeBytes, serverStorageKey, verifiedSha256, null, at);
    }
    public static Attachment AttachUrl(Guid id, Guid organizationId, Guid cardId, Guid uploaderId, string title, string url, DateTimeOffset at)
    {
        var value = url?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("https" or "http")
            || string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo) || parsed.AbsoluteUri.Length > 2048)
            throw new ArgumentException("Attachment URL must be an absolute HTTP(S) link without credentials.", nameof(url));
        // Metadata only: no URL fetch, server-side preview or storage operation.
        return new(id, organizationId, cardId, uploaderId, title, AttachmentKind.Url, null, null, null, null, parsed.AbsoluteUri, at);
    }
    public bool CompleteScan(AttachmentScanStatus verdict, DateTimeOffset at)
    {
        RequireFileActive();
        if (verdict is not (AttachmentScanStatus.Clean or AttachmentScanStatus.Rejected or AttachmentScanStatus.Failed))
            throw new ArgumentException("Scanner result must be terminal.", nameof(verdict));
        if (ScanStatus == verdict) return false;
        if (ScanStatus != AttachmentScanStatus.Pending) throw new InvalidOperationException("Attachment scan is already resolved.");
        MarkUpdated(at.ToUniversalTime()); ScanStatus = verdict; ScannedAt = UpdatedAt; return true;
    }
    public bool RetryFailedScan(DateTimeOffset at)
    {
        RequireFileActive();
        if (ScanStatus != AttachmentScanStatus.Failed) throw new InvalidOperationException("Only failed scanning can be retried.");
        MarkUpdated(at.ToUniversalTime()); ScanStatus = AttachmentScanStatus.Pending; ScannedAt = null; return true;
    }
    public bool Delete(DateTimeOffset at)
    {
        if (DeletedAt is not null) return false;
        MarkUpdated(at.ToUniversalTime()); DeletedAt = UpdatedAt; return true;
    }
    private void RequireFileActive()
    {
        if (Kind != AttachmentKind.File || DeletedAt is not null) throw new InvalidOperationException("Attachment scanning is unavailable.");
    }
    private static string RequireDisplayName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 255
            || normalized.Any(character => char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format))
            throw new ArgumentException("Attachment display name is required and must fit its limit.", nameof(value));
        return normalized;
    }
}
