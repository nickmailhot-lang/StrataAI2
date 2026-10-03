using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentPreviewProcessProtocolTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
    private static AttachmentScanRequest Request(byte[] source) => new(new(Guid.NewGuid(), Guid.NewGuid()), source.Length,
        Convert.ToHexStringLower(SHA256.HashData(source)));
    private static byte[] Frame(byte[] png, int width = 1, int height = 1, int? length = null)
    {
        var result = new byte[22 + png.Length]; "SAPRV001"u8.CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(10), width);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(14), height);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(18), length ?? png.Length); png.CopyTo(result, 22); return result;
    }

    [Fact]
    public async Task Private_source_pipe_contains_only_fixed_type_size_digest_and_exact_bytes()
    {
        var request = Request(Png); using var source = new MemoryStream(Png, writable: false); using var pipe = new MemoryStream();
        await AttachmentPreviewProcessProtocol.WriteSourceAsync(pipe, request, "image/png", source, TestContext.Current.CancellationToken);
        var actual = pipe.ToArray(); Assert.Equal(49 + Png.Length, actual.Length); Assert.Equal(1, actual[8]);
        Assert.Equal(Png.Length, BinaryPrimitives.ReadInt64LittleEndian(actual.AsSpan(9)));
        Assert.Equal(Convert.FromHexString(request.Sha256), actual[17..49]); Assert.Equal(Png, actual[49..]);
        Assert.DoesNotContain(request.Reference.OrganizationId.ToString("N"), Encoding.UTF8.GetString(actual), StringComparison.Ordinal);
        Assert.DoesNotContain(request.Reference.ObjectKey, Encoding.UTF8.GetString(actual), StringComparison.Ordinal);
        Assert.True(source.CanRead); Assert.True(pipe.CanWrite);
    }

    [Fact]
    public async Task Private_source_length_digest_and_type_refusals_never_claim_success()
    {
        var ct = TestContext.Current.CancellationToken; var request = Request(Png);
        using var source = new MemoryStream(Png, writable: false); using var pipe = new MemoryStream();
        Assert.Equal("preview_source_unavailable", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.WriteSourceAsync(pipe, new(request.Reference, request.SizeBytes + 1, request.Sha256), "image/png", source, ct))).Code);
        Assert.Equal(0, pipe.Length);
        Assert.Equal("preview_type_unsupported", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.WriteSourceAsync(pipe, request, "image/svg+xml", source, ct))).Code);
        Assert.Equal(0, pipe.Length);
        Assert.Equal("preview_source_unavailable", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.WriteSourceAsync(pipe, new(request.Reference, request.SizeBytes, new string('0', 64)), "image/png", source, ct))).Code);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Bounded_result_is_owned_read_only_and_hashes_actual_received_PNG()
    {
        using var pipe = new MemoryStream(Frame(Png));
        using var result = await AttachmentPreviewProcessProtocol.ReadResultAsync(pipe, TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Width); Assert.Equal(1, result.Height); Assert.Equal(Png.Length, result.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Png)), result.Sha256); Assert.False(result.Bytes.CanWrite);
        using var actual = new MemoryStream(); await result.Bytes.CopyToAsync(actual, TestContext.Current.CancellationToken); Assert.Equal(Png, actual.ToArray());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result)); Assert.False(json.RootElement.TryGetProperty("Bytes", out _));
        Assert.False(json.RootElement.TryGetProperty("Sha256", out _)); Assert.True(pipe.CanRead);
    }

    [Theory]
    [InlineData(0, 1, 70)] [InlineData(1025, 1, 70)] [InlineData(1, 0, 70)] [InlineData(1, 1025, 70)]
    [InlineData(1, 1, -1)] [InlineData(1, 1, 0)] [InlineData(1, 1, 8388609)]
    public async Task Invalid_dimensions_and_lengths_are_refused_before_payload_allocation(int width, int height, int length)
    {
        using var pipe = new MemoryStream(Frame([], width, height, length));
        Assert.Equal("preview_decoder_unavailable", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.ReadResultAsync(pipe, TestContext.Current.CancellationToken))).Code);
        Assert.Equal(22, pipe.Position);
    }

    [Theory]
    [InlineData(0)] [InlineData(16)] [InlineData(69)]
    public async Task Output_signature_dimensions_and_end_chunk_cannot_be_replaced(int position)
    {
        var corrupt = Png.ToArray(); corrupt[position] ^= 1; using var pipe = new MemoryStream(Frame(corrupt));
        Assert.Equal("preview_decoder_unavailable", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.ReadResultAsync(pipe, TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Trailing_output_and_private_diagnostic_failure_frames_are_refused()
    {
        using var extra = new MemoryStream(Frame(Png).Concat(new byte[] {1}).ToArray());
        Assert.Equal("preview_decoder_unavailable", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.ReadResultAsync(extra, TestContext.Current.CancellationToken))).Code);
        var failure = Frame([], length: 0); failure[8] = 1; failure[9] = 255;
        using var unknown = new MemoryStream(failure);
        Assert.Equal("preview_decoder_unavailable", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.ReadResultAsync(unknown, TestContext.Current.CancellationToken))).Code);
        failure[9] = 4; failure.AsSpan(10).Clear(); using var bounded = new MemoryStream(failure);
        Assert.Equal("preview_dimensions_exceeded", (await Assert.ThrowsAsync<AttachmentImagePreviewException>(() =>
            AttachmentPreviewProcessProtocol.ReadResultAsync(bounded, TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Failure_stage_is_a_closed_enum_and_cannot_contain_private_diagnostics()
    {
        var frame = Frame([], length: 0); frame[8] = 1; frame.AsSpan(10).Clear(); frame[10] = (byte)AttachmentPreviewFailureStage.FileSystemProbe;
        using var known = new MemoryStream(frame);
        var error = await Assert.ThrowsAsync<AttachmentImagePreviewException>(() => AttachmentPreviewProcessProtocol.ReadResultAsync(known, TestContext.Current.CancellationToken));
        Assert.Equal("preview_decoder_unavailable", error.Code); Assert.Equal(AttachmentPreviewFailureStage.FileSystemProbe, error.Stage); Assert.Null(error.InnerException);
        frame[10] = 255; using var unknown = new MemoryStream(frame);
        var refused = await Assert.ThrowsAsync<AttachmentImagePreviewException>(() => AttachmentPreviewProcessProtocol.ReadResultAsync(unknown, TestContext.Current.CancellationToken));
        Assert.Equal(AttachmentPreviewFailureStage.None, refused.Stage);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttachmentImagePreviewException("preview_decoder_unavailable", (AttachmentPreviewFailureStage)255));
    }

    [Fact]
    public async Task Precancelled_source_and_result_operations_propagate_cancellation()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var pipe = new MemoryStream(); using var source = new MemoryStream(Png);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentPreviewProcessProtocol.WriteSourceAsync(pipe, Request(Png), "image/png", source, cancelled.Token));
        using var result = new MemoryStream(Frame(Png));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentPreviewProcessProtocol.ReadResultAsync(result, cancelled.Token));
    }

    [Fact]
    public async Task Long_runtime_reports_are_drained_without_losing_the_bounded_failure_category()
    {
        // A fail-fast stack can exceed the retained prefix. Private trailing
        // details must not change the fixed category or remain in the pipe.
        var bytes = Encoding.UTF8.GetBytes("Out of memory.\n" + new string('x', 65536));
        using var pipe = new MemoryStream(bytes);
        Assert.Equal(AttachmentPreviewFailureStage.RuntimeMemory,
            await AttachmentPreviewProcessProtocol.DrainRuntimeFailureAsync(pipe, TestContext.Current.CancellationToken));
        Assert.Equal(pipe.Length, pipe.Position); Assert.True(pipe.CanRead);
    }

    [Fact]
    public async Task Unknown_diagnostics_and_failure_phrases_outside_the_retained_prefix_are_not_returned()
    {
        using var pipe = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 8192) + "Out of memory."));
        Assert.Equal(AttachmentPreviewFailureStage.None,
            await AttachmentPreviewProcessProtocol.DrainRuntimeFailureAsync(pipe, TestContext.Current.CancellationToken));
        Assert.Equal(pipe.Length, pipe.Position);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AttachmentPreviewProcessProtocol.DrainRuntimeFailureAsync(pipe, cancelled.Token));
    }

    [Fact]
    public async Task Last_recognized_fixed_child_progress_survives_an_unrecognized_abort_report()
    {
        var bytes = "SAPRVSTG"u8.ToArray().Concat(new[] {(byte)AttachmentPreviewFailureStage.RuntimeLaunch})
            .Concat("SAPRVSTG"u8.ToArray()).Concat(new[] {(byte)AttachmentPreviewFailureStage.Source})
            .Concat("SAPRVSTG"u8.ToArray()).Concat(new byte[] {255})
            .Concat(Encoding.UTF8.GetBytes("unrecognized private details")).ToArray();
        using var pipe = new MemoryStream(bytes);
        Assert.Equal(AttachmentPreviewFailureStage.Source,
            await AttachmentPreviewProcessProtocol.DrainRuntimeFailureAsync(pipe, TestContext.Current.CancellationToken));
        Assert.Equal(pipe.Length, pipe.Position);
    }
}
