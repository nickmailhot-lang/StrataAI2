using System.Globalization;
using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

// A queue reference carries no filename, digest, provider key, bytes or verdict.
// Worker must load canonical integrity under the exact current job lease.
public sealed record AttachmentScanAttempt(Guid AttachmentId, Guid CardId, long Version)
{
    public static AttachmentScanAttempt Parse(string json)
    {
        if (json is null || json.Length > 256) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
                || !root.TryGetProperty("attachmentId", out var attachment) || attachment.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(attachment.GetString(), "D", out var id) || id == Guid.Empty
                || !root.TryGetProperty("cardId", out var card) || card.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(card.GetString(), "D", out var parent) || parent == Guid.Empty
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt64(out var revision) || revision < 1 || revision == long.MaxValue) throw Invalid();
            return new(id, parent, revision);
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new("Attachment scan references are invalid.");
}

public interface IAttachmentScanJobPublisher
{
    // Owning tenant command only. Publication of Pending metadata, original Card
    // CAS, intent, audit/event and this job must succeed or roll back together.
    Task<bool> PublishScanAsync(AttachmentUploadRecord upload, AttachmentFileRecord file, Guid actor,
        string correlationId, CancellationToken ct);
}

public static class AttachmentScanJobs
{
    public const string Type = "ATTACHMENT_SCAN";
    public const string Service = "attachment-quarantine-scan";
    public static NewBackgroundJob Create(AttachmentUploadRecord upload, AttachmentFileRecord file, Guid actor, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(upload); ArgumentNullException.ThrowIfNull(file);
        var metadata = file.Metadata; var integrity = file.Integrity;
        if (metadata is null || integrity is null || upload.State != AttachmentUploadState.Published
            || upload.PublishedAt is null || upload.StoredAt is null || upload.WriteLeaseId is not null || upload.WriteLeaseUntil is not null
            || upload.AbandonedAt is not null || upload.PublishedAt < upload.StoredAt || upload.PublishedAt >= upload.ExpiresAt
            || upload.Version < 1 || upload.OriginalCardVersion < 1 || actor == Guid.Empty || actor != upload.UploaderId
            || metadata.Id != upload.Id || metadata.OrganizationId != upload.OrganizationId || metadata.CardId != upload.CardId
            || metadata.UploaderId != actor || metadata.DisplayName != upload.DisplayName || metadata.Kind != AttachmentKind.File
            || metadata.ScanStatus != AttachmentScanStatus.Pending || metadata.ScannedAt is not null || metadata.DeletedAt is not null
            || metadata.Version < 1 || metadata.Version == long.MaxValue || metadata.CreatedAt < upload.StoredAt || metadata.UpdatedAt != metadata.CreatedAt
            || metadata.CreatedAt > upload.PublishedAt || metadata.MimeType != upload.VerifiedMimeType
            || metadata.SizeBytes != upload.ExpectedSizeBytes || integrity.Reference.OrganizationId != upload.OrganizationId
            || integrity.Reference.AttachmentId != upload.Id || integrity.SizeBytes != upload.ExpectedSizeBytes
            || integrity.Sha256 != upload.ExpectedSha256 || upload.CardId == Guid.Empty || upload.RetryKey == Guid.Empty)
            throw new InvalidOperationException("Attachment scan publication is unavailable.");
        if (string.IsNullOrEmpty(correlationId) || correlationId.Length > 64
            || correlationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            throw new ArgumentException("Scan correlation identity is invalid.", nameof(correlationId));
        return new(Guid.NewGuid(), upload.OrganizationId, Type,
            $"attachment-scan/{upload.Id:N}/{metadata.Version.ToString(CultureInfo.InvariantCulture)}", actor, Service, correlationId,
            JsonSerializer.Serialize(new { attachmentId=upload.Id, cardId=upload.CardId, version=metadata.Version }));
    }
}
