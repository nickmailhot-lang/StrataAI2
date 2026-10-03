using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentUploadIntentTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Digest = new('a', 64);
    private static AttachmentUploadIntent Intent() => AttachmentUploadIntent.Prepare(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7, " Image.png ", 128, Digest, At.AddHours(1), At);
    private static object Snapshot(AttachmentUploadIntent intent) => (intent.State, intent.Version, intent.UpdatedAt, intent.WriteLeaseId, intent.WriteLeaseUntil, intent.VerifiedMimeType, intent.StoredAt, intent.PublishedAt, intent.AbandonedAt);
    [Fact]
    public void PRD_14_TC_01_Original_intent_and_bounded_writer_complete_verified_bytes_before_publication()
    {
        var intent = Intent(); var lease = Guid.NewGuid(); Assert.Equal("Image.png", intent.DisplayName); Assert.Equal(7, intent.CardVersion);
        Assert.Equal(AttachmentUploadState.Prepared, intent.State); Assert.Null(intent.VerifiedMimeType); Assert.Null(intent.StoredAt);
        intent.StartWrite(lease, At.AddMinutes(5), At); Assert.Equal(lease, intent.WriteLeaseId); Assert.Equal(2, intent.Version);
        Assert.True(intent.RenewWrite(lease, At.AddMinutes(6), At.AddMinutes(1))); Assert.Equal(3, intent.Version);
        Assert.False(intent.RenewWrite(lease, At.AddMinutes(6), At.AddMinutes(1))); Assert.Equal(3, intent.Version);
        intent.RecordStored(lease, "image/png", 128, Digest, At.AddMinutes(2));
        Assert.Equal(AttachmentUploadState.Stored, intent.State); Assert.Null(intent.WriteLeaseId); Assert.Null(intent.WriteLeaseUntil);
        Assert.Equal("image/png", intent.VerifiedMimeType); Assert.Equal(At.AddMinutes(2), intent.StoredAt); Assert.Null(intent.PublishedAt);
        Assert.True(intent.Publish(At.AddMinutes(3))); Assert.False(intent.Publish(At.AddHours(2)));
        Assert.Equal(AttachmentUploadState.Published, intent.State); Assert.Equal(At.AddMinutes(3), intent.PublishedAt); Assert.Equal(5, intent.Version);
        Assert.Equal(7, intent.CardVersion); Assert.Equal(128, intent.ExpectedSizeBytes); Assert.Equal(Digest, intent.ExpectedSha256);
        Assert.Throws<InvalidOperationException>(() => intent.Abandon(At.AddHours(2)));
    }
    [Fact]
    public void PRD_14_TC_06_Unknown_outcomes_require_reconciliation_and_cannot_start_a_second_writer()
    {
        var intent = Intent(); var lease = Guid.NewGuid(); intent.StartWrite(lease, At.AddMinutes(5), At);
        intent.RecordUnknownWrite(lease, At.AddMinutes(1)); Assert.Equal(AttachmentUploadState.Reconcile, intent.State); Assert.Null(intent.WriteLeaseId);
        var before = Snapshot(intent);
        Assert.Throws<InvalidOperationException>(() => intent.StartWrite(Guid.NewGuid(), At.AddMinutes(5), At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => intent.Publish(At.AddMinutes(2))); Assert.Equal(before, Snapshot(intent));
        intent.RecordReconciledObject("image/png", 128, Digest, At.AddMinutes(2)); Assert.Equal(AttachmentUploadState.Stored, intent.State);
        Assert.True(intent.Publish(At.AddMinutes(3))); Assert.Equal(7, intent.CardVersion);
    }
    [Fact]
    public void PRD_14_TC_06_Verified_absence_allows_original_identity_retry_and_old_writer_cannot_finish()
    {
        var intent = Intent(); var oldLease = Guid.NewGuid(); intent.StartWrite(oldLease, At.AddMinutes(1), At);
        Assert.Throws<InvalidOperationException>(() => intent.ReconcileExpiredWriter(At.AddSeconds(59)));
        intent.ReconcileExpiredWriter(At.AddMinutes(1)); Assert.Equal(AttachmentUploadState.Reconcile, intent.State);
        intent.ConfirmMissingObject(At.AddMinutes(2)); var lease = Guid.NewGuid(); intent.StartWrite(lease, At.AddMinutes(5), At.AddMinutes(2));
        var before = Snapshot(intent);
        Assert.Throws<InvalidOperationException>(() => intent.RecordStored(oldLease, "image/png", 128, Digest, At.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => intent.RecordUnknownWrite(oldLease, At.AddMinutes(3))); Assert.Equal(before, Snapshot(intent));
        intent.RecordStored(lease, "image/png", 128, Digest, At.AddMinutes(3)); Assert.True(intent.Publish(At.AddMinutes(4)));
    }
    [Theory]
    [InlineData("image/png", 127, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", typeof(InvalidOperationException))]
    [InlineData("image/png", 128, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", typeof(InvalidOperationException))]
    [InlineData("IMAGE/PNG", 128, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", typeof(InvalidOperationException))]
    [InlineData("image/png; private", 128, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", typeof(ArgumentException))]
    public void PRD_14_TC_03_Conflicting_measurements_never_mutate_writer_or_stored_state(string mime, long size, string hash, Type exceptionType)
    {
        var intent = Intent(); var lease = Guid.NewGuid(); intent.StartWrite(lease, At.AddMinutes(5), At); var before = Snapshot(intent);
        Assert.Throws(exceptionType, () => intent.RecordStored(lease, mime, size, hash, At.AddMinutes(1))); Assert.Equal(before, Snapshot(intent));
        intent.RecordUnknownWrite(lease, At.AddMinutes(1)); before = Snapshot(intent);
        Assert.Throws(exceptionType, () => intent.RecordReconciledObject(mime, size, hash, At.AddMinutes(2))); Assert.Equal(before, Snapshot(intent));
    }
    [Fact]
    public void ARCH_07_TC_01_Expired_intents_and_active_writers_cannot_publish_or_be_abandoned_mid_write()
    {
        var intent = Intent(); var lease = Guid.NewGuid(); intent.StartWrite(lease, At.AddMinutes(5), At); var before = Snapshot(intent);
        Assert.Throws<InvalidOperationException>(() => intent.Abandon(At.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => intent.RecordStored(lease, "image/png", 128, Digest, At.AddMinutes(5))); Assert.Equal(before, Snapshot(intent));
        intent.ReconcileExpiredWriter(At.AddHours(1)); before = Snapshot(intent);
        Assert.Throws<InvalidOperationException>(() => intent.ConfirmMissingObject(At.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => intent.RecordReconciledObject("image/png", 128, Digest, At.AddHours(1))); Assert.Equal(before, Snapshot(intent));
        Assert.True(intent.Abandon(At.AddHours(1))); Assert.False(intent.Abandon(At.AddHours(2))); Assert.Equal(AttachmentUploadState.Abandoned, intent.State);
        Assert.Equal(Digest, intent.ExpectedSha256); Assert.Equal(128, intent.ExpectedSizeBytes); Assert.Equal(At.AddHours(1), intent.AbandonedAt);
        Assert.Throws<InvalidOperationException>(() => intent.StartWrite(Guid.NewGuid(), At.AddHours(2).AddMinutes(1), At.AddHours(2)));
        var stored = Intent(); stored.StartWrite(lease, At.AddMinutes(5), At); stored.RecordStored(lease, "image/png", 128, Digest, At);
        Assert.Throws<InvalidOperationException>(() => stored.Publish(At.AddHours(1))); Assert.True(stored.Abandon(At.AddHours(1))); Assert.Equal("image/png", stored.VerifiedMimeType); Assert.Equal(At, stored.StoredAt);
    }
    [Fact]
    public void ARCH_07_TC_01_Bad_lease_windows_wrong_identity_and_backwards_time_do_not_partially_transition()
    {
        var intent = Intent(); var before = Snapshot(intent);
        foreach (var lease in new[] { (Guid.Empty, At.AddMinutes(1)), (Guid.NewGuid(), At), (Guid.NewGuid(), At.AddMinutes(11)), (Guid.NewGuid(), At.AddHours(2)) })
        { Assert.Throws<InvalidOperationException>(() => intent.StartWrite(lease.Item1, lease.Item2, At)); Assert.Equal(before, Snapshot(intent)); }
        var writer = Guid.NewGuid(); intent.StartWrite(writer, At.AddMinutes(5), At.AddMinutes(1)); before = Snapshot(intent);
        Assert.Throws<InvalidOperationException>(() => intent.RenewWrite(Guid.NewGuid(), At.AddMinutes(6), At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => intent.RenewWrite(writer, At.AddMinutes(4), At.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => intent.RecordUnknownWrite(writer, At)); Assert.Equal(before, Snapshot(intent));
    }
    [Theory]
    [InlineData(0, 128)]
    [InlineData(7, 0)]
    [InlineData(7, 1073741825)]
    public void PRD_14_TC_03_Original_revision_and_declared_size_must_fit_domain_bounds(long revision, long size) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        AttachmentUploadIntent.Prepare(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), revision, "Image", size, Digest, At.AddHours(1), At));
    [Fact]
    public void PRD_14_TC_03_Empty_scope_bad_digest_display_spoof_and_unbounded_expiry_are_rejected()
    {
        var id = Guid.NewGuid(); var organization = Guid.NewGuid(); var card = Guid.NewGuid(); var actor = Guid.NewGuid(); var key = Guid.NewGuid();
        foreach (var scope in new[] { (Guid.Empty, organization, card, actor, key), (id, Guid.Empty, card, actor, key), (id, organization, Guid.Empty, actor, key), (id, organization, card, Guid.Empty, key), (id, organization, card, actor, Guid.Empty) })
            Assert.Throws<ArgumentException>(() => AttachmentUploadIntent.Prepare(scope.Item1, scope.Item2, scope.Item3, scope.Item4, scope.Item5, 7, "Image", 128, Digest, At.AddHours(1), At));
        Assert.Throws<ArgumentException>(() => AttachmentUploadIntent.Prepare(id, organization, card, actor, key, 7, "Image", 128, "private-input", At.AddHours(1), At));
        Assert.Throws<ArgumentException>(() => AttachmentUploadIntent.Prepare(id, organization, card, actor, key, 7, "Image\u202Egnp.exe", 128, Digest, At.AddHours(1), At));
        foreach (var expires in new[] { At, At.AddTicks(-1), At.AddDays(1).AddTicks(1) })
            Assert.Throws<ArgumentOutOfRangeException>(() => AttachmentUploadIntent.Prepare(id, organization, card, actor, key, 7, "Image", 128, Digest, expires, At));
    }
}
