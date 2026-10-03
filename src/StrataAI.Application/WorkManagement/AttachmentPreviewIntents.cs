using System.Text.Json;
using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.WorkManagement;

public static class AttachmentPreviewJobs
{
    public const string Type = "ATTACHMENT_PREVIEW";
    public const string Service = "attachment-private-preview";
}

// Only opaque identities belong in the queue. This parser is not authorization.
public sealed record AttachmentPreviewAttempt(Guid AttachmentId, Guid CardId, long Version)
{
    public static AttachmentPreviewAttempt Parse(string json)
    {
        if (json is null || json.Length > 256) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
                || !root.TryGetProperty("attachmentId", out var attachment) || attachment.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(attachment.GetString(), "D", out var id) || id == Guid.Empty || attachment.GetString() != id.ToString("D")
                || !root.TryGetProperty("cardId", out var card) || card.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(card.GetString(), "D", out var parent) || parent == Guid.Empty || card.GetString() != parent.ToString("D")
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt64(out var revision) || revision < 2 || revision == long.MaxValue
                || version.GetRawText() != revision.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw Invalid();
            return new(id, parent, revision);
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new("Attachment preview references are invalid.");
}

public sealed record AttachmentPreviewMeasurement
{
    public AttachmentPreviewMeasurement(long sizeBytes, string sha256, int width, int height)
    {
        if (sizeBytes is < 45 or > 8388608 || sha256 is null || sha256.Length != 64
            || sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            || width is < 1 or > 1024 || height is < 1 or > 1024)
            throw new ArgumentException("Attachment preview measurement is invalid.");
        SizeBytes = sizeBytes; Sha256 = sha256; Width = width; Height = height;
    }
    public long SizeBytes { get; }
    [System.Text.Json.Serialization.JsonIgnore] public string Sha256 { get; }
    public int Width { get; }
    public int Height { get; }
}

public enum AttachmentPreviewLoadStatus { Ready, Superseded, LeaseLost }
public sealed record AttachmentPreviewLoad(AttachmentPreviewLoadStatus Status, AttachmentScanRequest? Source = null,
    string? VerifiedMimeType = null, AttachmentPreviewMeasurement? DeclaredOutput = null);
public enum AttachmentPreviewDeclaration { Declared, Conflict, Superseded, LeaseLost }

public interface IAttachmentPreviewIntentStore
{
    // The exact live lease alone admits canonical Clean source integrity.
    // Existing output measurements permit recovery of unknown provider writes.
    Task<AttachmentPreviewLoad> LoadAsync(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt, CancellationToken ct);
    // Short transaction only. No provider I/O, publication, Card effects or enqueue.
    Task<AttachmentPreviewDeclaration> DeclareAsync(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt,
        AttachmentScanRequest source, string verifiedMimeType, AttachmentPreviewMeasurement output, CancellationToken ct);
}