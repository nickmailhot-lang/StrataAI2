using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

public enum AttachmentUploadState { Prepared, Writing, Reconcile, Stored, Published, Abandoned }

// Durable original intent must exist before provider I/O. Expected size/digest
// are untrusted retry-binding claims until complete server measurement matches.
// Application owns current authorization, DB CAS and transactional job/outbox.
public sealed class AttachmentUploadIntent : DomainEntity, IOrganizationScoped
{
    private AttachmentUploadIntent(Guid id, Guid organization, Guid card, Guid uploader, Guid retryKey,
        long cardVersion, string displayName, long expectedSize, string expectedSha256, DateTimeOffset expires, DateTimeOffset at)
        : base(id, at.ToUniversalTime())
    {
        if (organization == Guid.Empty || card == Guid.Empty || uploader == Guid.Empty || retryKey == Guid.Empty)
            throw new ArgumentException("Upload intent scope, uploader and retry identity are required.");
        if (cardVersion < 1) throw new ArgumentOutOfRangeException(nameof(cardVersion));
        if (expectedSize is < 1 or > 1073741824) throw new ArgumentOutOfRangeException(nameof(expectedSize));
        if (expectedSha256 is null || expectedSha256.Length != 64 || expectedSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical upload intent digest is required.", nameof(expectedSha256));
        expires = expires.ToUniversalTime();
        if (expires <= CreatedAt || expires - CreatedAt > TimeSpan.FromHours(24)) throw new ArgumentOutOfRangeException(nameof(expires));
        OrganizationId = organization; CardId = card; UploaderId = uploader; RetryKey = retryKey; CardVersion = cardVersion;
        DisplayName = Attachment.RequireDisplayName(displayName); ExpectedSizeBytes = expectedSize; ExpectedSha256 = expectedSha256; ExpiresAt = expires;
    }
    public static AttachmentUploadIntent Prepare(Guid id, Guid organization, Guid card, Guid uploader, Guid retryKey,
        long cardVersion, string displayName, long expectedSize, string expectedSha256, DateTimeOffset expires, DateTimeOffset at)
        => new(id, organization, card, uploader, retryKey, cardVersion, displayName, expectedSize, expectedSha256, expires, at);
    public Guid OrganizationId { get; }
    public Guid CardId { get; }
    public Guid UploaderId { get; }
    public Guid RetryKey { get; }
    public long CardVersion { get; }
    public string DisplayName { get; }
    public long ExpectedSizeBytes { get; }
    public string ExpectedSha256 { get; }
    public DateTimeOffset ExpiresAt { get; }
    public AttachmentUploadState State { get; private set; }
    public Guid? WriteLeaseId { get; private set; }
    public DateTimeOffset? WriteLeaseUntil { get; private set; }
    public string? VerifiedMimeType { get; private set; }
    public DateTimeOffset? StoredAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? AbandonedAt { get; private set; }

    public void StartWrite(Guid lease, DateTimeOffset until, DateTimeOffset at)
    {
        at = RequireTime(at); RequireUnexpired(at);
        if (State != AttachmentUploadState.Prepared) throw Unavailable();
        RequireLeaseWindow(lease, until, at);
        MarkUpdated(at); State = AttachmentUploadState.Writing; WriteLeaseId = lease; WriteLeaseUntil = until.ToUniversalTime();
    }
    public bool RenewWrite(Guid lease, DateTimeOffset until, DateTimeOffset at)
    {
        at = RequireTime(at); RequireActiveWriter(lease, at); RequireLeaseWindow(lease, until, at);
        until = until.ToUniversalTime();
        if (until == WriteLeaseUntil) return false;
        if (until < WriteLeaseUntil) throw Unavailable();
        MarkUpdated(at); WriteLeaseUntil = until; return true;
    }
    public void RecordStored(Guid lease, string verifiedMime, long measuredSize, string measuredSha256, DateTimeOffset at)
    {
        at = RequireTime(at); RequireActiveWriter(lease, at);
        RequireMeasurement(verifiedMime, measuredSize, measuredSha256, at);
        MarkUpdated(at); VerifiedMimeType = verifiedMime; StoredAt = at; State = AttachmentUploadState.Stored; ClearLease();
    }
    public void RecordUnknownWrite(Guid lease, DateTimeOffset at)
    {
        at = RequireTime(at);
        if (State != AttachmentUploadState.Writing || lease == Guid.Empty || lease != WriteLeaseId) throw Unavailable();
        MarkUpdated(at); State = AttachmentUploadState.Reconcile; ClearLease();
    }
    public void ReconcileExpiredWriter(DateTimeOffset at)
    {
        at = RequireTime(at);
        if (State != AttachmentUploadState.Writing || at < WriteLeaseUntil) throw Unavailable();
        MarkUpdated(at); State = AttachmentUploadState.Reconcile; ClearLease();
    }
    public void ConfirmMissingObject(DateTimeOffset at)
    {
        at = RequireTime(at); RequireUnexpired(at);
        if (State != AttachmentUploadState.Reconcile) throw Unavailable();
        // A verified private provider read established absence. Only now may a
        // new writer use the original identity; unknown outcomes are not absence.
        MarkUpdated(at); State = AttachmentUploadState.Prepared;
    }
    public void RecordReconciledObject(string verifiedMime, long measuredSize, string measuredSha256, DateTimeOffset at)
    {
        at = RequireTime(at); RequireUnexpired(at);
        if (State != AttachmentUploadState.Reconcile) throw Unavailable();
        RequireMeasurement(verifiedMime, measuredSize, measuredSha256, at);
        MarkUpdated(at); VerifiedMimeType = verifiedMime; StoredAt = at; State = AttachmentUploadState.Stored;
    }
    public bool Publish(DateTimeOffset at)
    {
        at = RequireTime(at);
        if (State == AttachmentUploadState.Published) return false;
        RequireUnexpired(at);
        if (State != AttachmentUploadState.Stored) throw Unavailable();
        // Caller must atomically re-admit current permission/parent lifecycle,
        // CAS original CardVersion, persist Pending metadata, audit and scan job.
        MarkUpdated(at); State = AttachmentUploadState.Published; PublishedAt = at; return true;
    }
    public bool Abandon(DateTimeOffset at)
    {
        at = RequireTime(at);
        if (State == AttachmentUploadState.Abandoned) return false;
        if (State is AttachmentUploadState.Writing or AttachmentUploadState.Published) throw Unavailable();
        // Retains claims/measurements for private orphan reconciliation. It
        // authorizes no deletion of a published attachment or foreign object.
        MarkUpdated(at); State = AttachmentUploadState.Abandoned; AbandonedAt = at; ClearLease(); return true;
    }
    private void RequireMeasurement(string mime, long size, string digest, DateTimeOffset at)
    {
        if (size != ExpectedSizeBytes || !string.Equals(digest, ExpectedSha256, StringComparison.Ordinal)) throw Unavailable();
        var validated = Attachment.QuarantineFile(Id, OrganizationId, CardId, UploaderId, DisplayName,
            mime, size, $"attachments/{OrganizationId:N}/{Id:N}", digest, at);
        if (!string.Equals(mime, validated.MimeType, StringComparison.Ordinal)) throw Unavailable();
    }
    private DateTimeOffset RequireTime(DateTimeOffset at)
    {
        at = at.ToUniversalTime();
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(at), "Upload timestamp cannot move backwards.");
        return at;
    }
    private void RequireUnexpired(DateTimeOffset at) { if (at >= ExpiresAt) throw Unavailable(); }
    private void RequireActiveWriter(Guid lease, DateTimeOffset at)
    {
        RequireUnexpired(at);
        if (State != AttachmentUploadState.Writing || lease == Guid.Empty || lease != WriteLeaseId || at >= WriteLeaseUntil) throw Unavailable();
    }
    private void RequireLeaseWindow(Guid lease, DateTimeOffset until, DateTimeOffset at)
    { if (lease == Guid.Empty || until <= at || until > ExpiresAt || until - at > TimeSpan.FromMinutes(10)) throw Unavailable(); }
    private void ClearLease() { WriteLeaseId = null; WriteLeaseUntil = null; }
    private static InvalidOperationException Unavailable() => new("Attachment upload transition is unavailable.");
}
