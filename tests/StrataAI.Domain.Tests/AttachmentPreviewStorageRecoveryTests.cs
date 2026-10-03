using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentPreviewStorageRecoveryTests
{
    [Fact]
    public async Task Private_output_is_declared_before_write_and_independently_verified_before_return()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = new Fixture();
        var result = await f.Run();
        Assert.Equal(new(f.Job.OrganizationId, f.Job.Id), result!.Reference);
        Assert.Equal(f.Intent.Output, result.Measurement); Assert.Equal(1, f.Storage.Writes);
        Assert.Equal(1, f.Generator.Calls); Assert.Equal(1, f.Intent.Declarations);
        Assert.Equal(0, f.Storage.Deletes); Assert.Equal(1, f.Storage.SourceReads);
        Assert.True(f.Storage.ArtifactReads >= 2);
        Assert.Throws<ObjectDisposedException>(() => f.Generator.Last!.Bytes.ReadByte());
        Assert.Equal("{}", JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task Unknown_committed_write_is_recovered_without_decoding_or_writing_again()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = new Fixture(); f.Storage.FailAfterCommit = true;
        await Assert.ThrowsAsync<AttachmentStorageException>(() => f.Run());
        Assert.NotNull(f.Intent.Output); Assert.Equal(1, f.Storage.Writes);
        var result = await f.Run();
        Assert.NotNull(result); Assert.Equal(1, f.Storage.Writes); Assert.Equal(1, f.Generator.Calls);
        Assert.Equal(1, f.Storage.SourceReads); Assert.Equal(0, f.Storage.Deletes);
    }
    [Fact]
    public async Task Missing_unknown_write_retries_only_the_exact_declared_encoding()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = new Fixture(); f.Storage.FailBeforeCommit = true;
        await Assert.ThrowsAsync<AttachmentStorageException>(() => f.Run());
        var measurement = f.Intent.Output; Assert.NotNull(measurement);
        Assert.NotNull(await f.Run()); Assert.Equal(measurement, f.Intent.Output);
        Assert.Equal(2, f.Storage.Writes); Assert.Equal(2, f.Generator.Calls); Assert.Equal(0, f.Storage.Deletes);
    }
    [Fact]
    public async Task Corrupt_existing_artifact_is_retained_without_overwrite_or_cleanup()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = new Fixture(); await f.Run();
        f.Storage.Values[new(f.Job.OrganizationId, f.Job.Id)][0] ^= 1;
        await Assert.ThrowsAsync<AttachmentStorageException>(() => f.Run());
        Assert.Equal(1, f.Storage.Writes); Assert.Equal(1, f.Generator.Calls);
        Assert.NotNull(f.Intent.Output); Assert.Equal(0, f.Storage.Deletes);
    }
    [Fact]
    public async Task Different_retry_encoding_cannot_replace_an_immutable_declaration()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = new Fixture(); f.Storage.FailBeforeCommit = true;
        await Assert.ThrowsAsync<AttachmentStorageException>(() => f.Run());
        var original = f.Intent.Output; f.Generator.Encoding[0] = 1;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        Assert.Equal("Attachment preview storage recovery is unavailable.", error.Message);
        Assert.Equal(original, f.Intent.Output); Assert.Equal(1, f.Storage.Writes);
        Assert.Equal(0, f.Storage.Deletes);
    }
    [Theory]
    [InlineData(AttachmentPreviewLoadStatus.LeaseLost)]
    [InlineData(AttachmentPreviewLoadStatus.Superseded)]
    public async Task Withdrawn_initial_admission_never_reads_private_provider_bytes(AttachmentPreviewLoadStatus status)
    {
        var f = new Fixture(); f.Intent.Status = status;
        if (status == AttachmentPreviewLoadStatus.Superseded) Assert.Null(await f.Run());
        else await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        Assert.Equal(0, f.Storage.SourceReads); Assert.Equal(0, f.Storage.ArtifactReads);
        Assert.Equal(0, f.Generator.Calls); Assert.Equal(0, f.Storage.Writes);
    }
    [Fact]
    public async Task Parent_withdrawal_after_declaration_prevents_provider_write_and_retains_recovery()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = new Fixture(); f.Intent.WithdrawAtLoad = 3;
        Assert.Null(await f.Run()); Assert.NotNull(f.Intent.Output);
        Assert.Equal(0, f.Storage.Writes); Assert.Equal(0, f.Storage.Deletes);
    }
    [Fact]
    public async Task Wrong_provider_receipt_never_returns_stored_evidence_but_retry_verifies_actual_bytes()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = new Fixture(); f.Storage.WrongReceipt = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        Assert.NotNull(await f.Run()); Assert.Equal(1, f.Storage.Writes);
        Assert.Equal(1, f.Generator.Calls); Assert.Equal(0, f.Storage.Deletes);
    }
    [Fact]
    public async Task Cancellation_before_admission_has_no_provider_effects()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Coordinator.EnsureStoredAsync(f.Job, f.Attempt, cancellation.Token));
        Assert.Equal(0, f.Storage.Writes); Assert.Equal(0, f.Storage.SourceReads);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var source = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); var card = Guid.NewGuid();
            Job = new(Guid.NewGuid(), source.OrganizationId, AttachmentPreviewJobs.Type, Guid.NewGuid(), AttachmentPreviewJobs.Service,
                "preview-recovery", JsonSerializer.Serialize(new { attachmentId = source.AttachmentId, cardId = card, version = 2 }),
                1, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));
            Attempt = AttachmentPreviewAttempt.Parse(Job.SafeMetadataJson);
            var bytes = new byte[128]; var request = new AttachmentScanRequest(source, bytes.Length, Hash(bytes));
            Intent = new(request); Storage = new(source, Intent); Storage.Values.Add(source, bytes);
            Coordinator = new(Intent, new PrivateAttachmentDownloadPreparer(Storage), Generator, Storage);
        }
        public ClaimedBackgroundJob Job { get; }
        public AttachmentPreviewAttempt Attempt { get; }
        public IntentStore Intent { get; }
        public Storage Storage { get; }
        public Generator Generator { get; } = new();
        public AttachmentPreviewStorageRecovery Coordinator { get; }
        public Task<AttachmentPreviewStoredOutput?> Run() => Coordinator.EnsureStoredAsync(Job, Attempt, TestContext.Current.CancellationToken);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class IntentStore(AttachmentScanRequest source) : IAttachmentPreviewIntentStore
    {
        public AttachmentPreviewLoadStatus Status { get; set; } = AttachmentPreviewLoadStatus.Ready;
        public AttachmentPreviewMeasurement? Output { get; private set; }
        public int Loads { get; private set; }
        public int Declarations { get; private set; }
        public int? WithdrawAtLoad { get; set; }
        public Task<AttachmentPreviewLoad> LoadAsync(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Loads++;
            if (Loads == WithdrawAtLoad) Status = AttachmentPreviewLoadStatus.Superseded;
            return Task.FromResult(Status == AttachmentPreviewLoadStatus.Ready ? new(Status, source, "image/png", Output) : new AttachmentPreviewLoad(Status));
        }
        public Task<AttachmentPreviewDeclaration> DeclareAsync(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt,
            AttachmentScanRequest original, string mime, AttachmentPreviewMeasurement output, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Declarations++; Assert.Equal(source, original); Assert.Equal("image/png", mime);
            if (Output is not null && Output != output) return Task.FromResult(AttachmentPreviewDeclaration.Conflict);
            Output = output; return Task.FromResult(AttachmentPreviewDeclaration.Declared);
        }
    }
    private sealed class Generator : IAttachmentImagePreviewGenerator
    {
        public byte[] Encoding { get; } = new byte[70];
        public int Calls { get; private set; }
        public AttachmentPreviewImage? Last { get; private set; }
        public Task<AttachmentPreviewImage> GenerateAsync(AttachmentScanRequest request, string mime, Stream source, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Assert.True(source.CanRead); Assert.True(source.CanSeek); Assert.False(source.CanWrite);
            Calls++; Last = new(Encoding.ToArray(), 1, 1); return Task.FromResult(Last);
        }
    }
    private sealed class Storage(AttachmentObjectReference source, IntentStore intent) : IAttachmentObjectStorage
    {
        public Dictionary<AttachmentObjectReference, byte[]> Values { get; } = [];
        public int SourceReads { get; private set; }
        public int ArtifactReads { get; private set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }
        public bool FailBeforeCommit { get; set; }
        public bool FailAfterCommit { get; set; }
        public bool WrongReceipt { get; set; }
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Assert.Equal(source.OrganizationId, reference.OrganizationId);
            if (reference == source) SourceReads++; else ArtifactReads++;
            return Task.FromResult<Stream?>(Values.TryGetValue(reference, out var bytes) ? new MemoryStream(bytes, false) : null);
        }
        public async Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream bytes, long maximum, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Writes++; Assert.NotNull(intent.Output);
            Assert.NotEqual(source, reference); Assert.Equal(source.OrganizationId, reference.OrganizationId);
            Assert.Equal(intent.Output.SizeBytes, maximum); Assert.False(Values.ContainsKey(reference));
            if (FailBeforeCommit) { FailBeforeCommit = false; throw new AttachmentStorageException("object_storage_unavailable"); }
            using var content = new MemoryStream(); await bytes.CopyToAsync(content, ct);
            var encoded = content.ToArray(); Assert.Equal(maximum, encoded.Length); Assert.Equal(intent.Output.Sha256, Hash(encoded));
            Values.Add(reference, encoded);
            if (FailAfterCommit) { FailAfterCommit = false; throw new AttachmentStorageException("object_storage_unavailable"); }
            return new(reference, encoded.Length + (WrongReceipt ? 1 : 0), Hash(encoded));
        }
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct)
        {
            Deletes++; throw new InvalidOperationException("Preview recovery must not delete uncertain objects.");
        }
    }
}
