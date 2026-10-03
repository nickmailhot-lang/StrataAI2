using StrataAI.Domain.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private readonly Guid organization = Guid.NewGuid(), card = Guid.NewGuid(), actor = Guid.NewGuid();
    private Attachment File(string mime = "image/png") => Attachment.QuarantineFile(Guid.NewGuid(), organization, card, actor,
        " Preparation image ", mime, 128, "attachments/server-random-key", new string('a', 64), At);

    [Theory]
    [InlineData(AttachmentScanStatus.Pending)]
    [InlineData(AttachmentScanStatus.NotApplicable)]
    [InlineData((AttachmentScanStatus)99)]
    public void PRD_14_TC_03_UnknownScannerResultsCannotReleaseQuarantine(AttachmentScanStatus verdict)
    {
        var file = File(); Assert.Throws<ArgumentException>(() => file.CompleteScan(verdict, At));
        Assert.Equal(AttachmentScanStatus.Pending, file.ScanStatus); Assert.Equal(1, file.Version); Assert.False(file.CanDownload);
    }
    [Fact]
    public void PRD_14_TC_03_ScopeIdentityAndCanonicalUrlLimitsAreRequired()
    {
        foreach (var values in new[] { (Guid.Empty, card, actor), (organization, Guid.Empty, actor), (organization, card, Guid.Empty) })
            Assert.Throws<ArgumentException>(() => Attachment.AttachUrl(Guid.NewGuid(), values.Item1, values.Item2, values.Item3, "Reference", "https://example.test/", At));
        Assert.Throws<ArgumentException>(() => Attachment.AttachUrl(Guid.Empty, organization, card, actor, "Reference", "https://example.test/", At));
        // Unicode percent-encoding must not exceed the persisted URL bound.
        Assert.Throws<ArgumentException>(() => Attachment.AttachUrl(Guid.NewGuid(), organization, card, actor, "Reference", "https://example.test/" + new string('成', 300), At));
    }
    [Theory]
    [InlineData("Review\nplan")]
    [InlineData("Image\u202Egnp.exe")]
    [InlineData("")]
    public void PRD_14_TC_03_DisplayNamesRejectControlAndDirectionSpoofing(string title) => Assert.Throws<ArgumentException>(() =>
        Attachment.AttachUrl(Guid.NewGuid(), organization, card, actor, title, "https://example.test/", At));

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void PRD_14_TC_01_FileQuarantinePreventsDeliveryAndCoverUntilClean(string mime)
    {
        var file = File(mime);
        Assert.Equal(organization, file.OrganizationId); Assert.Equal(card, file.CardId); Assert.Equal(actor, file.UploaderId);
        Assert.Equal("Preparation image", file.DisplayName); Assert.Equal(128, file.SizeBytes);
        Assert.Equal(new string('a', 64), file.Sha256);
        Assert.Equal(AttachmentKind.File, file.Kind); Assert.Equal(1, file.Version); Assert.Null(file.Url);
        Assert.False(file.CanDownload); Assert.False(file.CanPreviewImage); Assert.False(file.CanUseAsCoverFor(organization, card));
        Assert.True(file.CompleteScan(AttachmentScanStatus.Clean, At.AddMinutes(1)));
        Assert.True(file.CanDownload); Assert.True(file.CanPreviewImage); Assert.True(file.CanUseAsCoverFor(organization, card));
        Assert.False(file.CanUseAsCoverFor(Guid.NewGuid(), card)); Assert.False(file.CanUseAsCoverFor(organization, Guid.NewGuid()));
        Assert.Equal(2, file.Version); Assert.Equal(At.AddMinutes(1), file.ScannedAt);
        Assert.False(file.CompleteScan(AttachmentScanStatus.Clean, At.AddMinutes(2))); Assert.Equal(2, file.Version);
    }
    [Fact]
    public void PRD_14_TC_01_NonImagesAreNeverCoversEvenWhenClean()
    {
        var file = File("application/pdf"); file.CompleteScan(AttachmentScanStatus.Clean, At);
        Assert.True(file.CanDownload); Assert.False(file.CanPreviewImage); Assert.False(file.CanUseAsCoverFor(organization, card));
    }
    [Theory]
    [InlineData(AttachmentScanStatus.Rejected)]
    [InlineData(AttachmentScanStatus.Failed)]
    public void PRD_14_TC_03_ScannerRefusalAndFailureRemainUnavailable(AttachmentScanStatus verdict)
    {
        var file = File(); file.CompleteScan(verdict, At.AddMinutes(1));
        Assert.False(file.CanDownload); Assert.False(file.CanUseAsCoverFor(organization, card));
        Assert.Throws<InvalidOperationException>(() => file.CompleteScan(AttachmentScanStatus.Clean, At.AddMinutes(2)));
        Assert.Equal(verdict, file.ScanStatus); Assert.Equal(2, file.Version);
    }
    [Fact]
    public void PRD_14_TC_06_FailedScannerRequiresExplicitPendingRetryAndNeverClearsMalwareRejection()
    {
        var file = File(); file.CompleteScan(AttachmentScanStatus.Failed, At);
        file.RetryFailedScan(At.AddMinutes(1)); Assert.Equal(AttachmentScanStatus.Pending, file.ScanStatus);
        Assert.Null(file.ScannedAt); Assert.False(file.CanDownload);
        file.CompleteScan(AttachmentScanStatus.Clean, At.AddMinutes(2)); Assert.True(file.CanDownload);
        var rejected = File(); rejected.CompleteScan(AttachmentScanStatus.Rejected, At);
        Assert.Throws<InvalidOperationException>(() => rejected.RetryFailedScan(At.AddMinutes(1))); Assert.False(rejected.CanDownload);
    }
    [Fact]
    public void PRD_14_TC_10_TombstoneRevokesDeliveryAndPreservesRetainedMetadata()
    {
        var file = File(); file.CompleteScan(AttachmentScanStatus.Clean, At);
        Assert.True(file.Delete(At.AddMinutes(1))); Assert.False(file.Delete(At.AddMinutes(2)));
        Assert.False(file.CanDownload); Assert.False(file.CanPreviewImage); Assert.False(file.CanUseAsCoverFor(organization, card));
        Assert.Equal("attachments/server-random-key", file.StorageKey); Assert.Equal(actor, file.UploaderId); Assert.Equal(3, file.Version);
        Assert.Throws<InvalidOperationException>(() => file.CompleteScan(AttachmentScanStatus.Clean, At.AddMinutes(3)));
    }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,private")]
    [InlineData("file:///private.txt")]
    [InlineData("ftp://example.test/file")]
    [InlineData("https://user:secret@example.test/file")]
    [InlineData("/relative-link")]
    [InlineData("https://example.test/\r\nprivate")]
    public void PRD_14_TC_03_UnsafeUrlFormsFailWithoutEchoingInput(string url)
    {
        var error = Assert.Throws<ArgumentException>(() => Attachment.AttachUrl(Guid.NewGuid(), organization, card, actor, "Reference", url, At));
        Assert.DoesNotContain("secret", error.Message); Assert.DoesNotContain("private", error.Message);
    }
    [Fact]
    public void PRD_14_TC_01_UrlMetadataDoesNotPretendToBeScannedBinaryContent()
    {
        var link = Attachment.AttachUrl(Guid.NewGuid(), organization, card, actor, " Reference ", " https://example.test/path?q=1#section ", At);
        Assert.Equal("https://example.test/path?q=1#section", link.Url); Assert.Equal("Reference", link.DisplayName);
        Assert.Null(link.StorageKey); Assert.Null(link.MimeType); Assert.Null(link.SizeBytes);
        Assert.Null(link.Sha256);
        Assert.Equal(AttachmentScanStatus.NotApplicable, link.ScanStatus);
        Assert.False(link.CanDownload); Assert.False(link.CanUseAsCoverFor(organization, card));
        Assert.Throws<InvalidOperationException>(() => link.CompleteScan(AttachmentScanStatus.Clean, At));
    }
    [Fact]
    public void PRD_14_TC_03_RejectedTimestampDoesNotPartiallyAdvanceScanOrDeletion()
    {
        var file = File();
        Assert.Throws<ArgumentOutOfRangeException>(() => file.CompleteScan(AttachmentScanStatus.Clean, At.AddSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Delete(At.AddSeconds(-1)));
        Assert.Equal(AttachmentScanStatus.Pending, file.ScanStatus); Assert.Equal(1, file.Version); Assert.Null(file.DeletedAt); Assert.Null(file.ScannedAt);
    }
    [Theory]
    [InlineData("../private")]
    [InlineData("/absolute")]
    [InlineData("folder//key")]
    [InlineData("folder\\key")]
    [InlineData("folder/./key")]
    public void PRD_14_TC_03_StorageKeysCannotBeTraversalPaths(string key) => Assert.Throws<ArgumentException>(() =>
        Attachment.QuarantineFile(Guid.NewGuid(), organization, card, actor, "Name", "image/png", 128, key, new string('a', 64), At));
    [Theory]
    [InlineData("image/png; charset=secret", 128)]
    [InlineData("not-mime", 128)]
    [InlineData("image/png\r\nsecret", 128)]
    [InlineData("image/png", 0)]
    [InlineData("image/png", -1)]
    [InlineData("image/png", 1073741825)]
    public void PRD_14_TC_03_VerifiedMetadataMustHaveCanonicalMimeAndPositiveBytes(string mime, long size) => Assert.ThrowsAny<ArgumentException>(() =>
        Attachment.QuarantineFile(Guid.NewGuid(), organization, card, actor, "Name", mime, size, "server-key", new string('a', 64), At));
    [Theory]
    [InlineData("")]
    [InlineData("private-invalid-digest")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void PRD_14_TC_03_BinaryIntegrityClaimsRequireCanonicalMeasuredDigest(string digest) => Assert.Throws<ArgumentException>(() =>
        Attachment.QuarantineFile(Guid.NewGuid(), organization, card, actor, "Name", "image/png", 128, "server-key", digest, At));
}
