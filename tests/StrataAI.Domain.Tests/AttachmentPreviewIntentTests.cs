using System.Text.Json;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentPreviewIntentTests
{
    [Fact]
    public void Canonical_queue_reference_contains_only_three_opaque_fields()
    {
        var file = Guid.NewGuid(); var card = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new { attachmentId = file, cardId = card, version = 2 });
        Assert.Equal(new AttachmentPreviewAttempt(file, card, 2), AttachmentPreviewAttempt.Parse(json));
    }
    [Theory]
    [InlineData("[]")][InlineData("null")][InlineData("{}")]
    [InlineData("1")][InlineData("\"2\"")][InlineData("2.0")][InlineData("2e0")]
    [InlineData("9223372036854775807")]
    public void Invalid_shape_or_noncanonical_revision_has_one_fixed_failure(string invalid)
    {
        var json = invalid is "[]" or "null" or "{}" ? invalid :
            "{\"attachmentId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"cardId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"version\":" + invalid + "}";
        var error = Assert.Throws<InvalidOperationException>(() => AttachmentPreviewAttempt.Parse(json));
        Assert.Equal("Attachment preview references are invalid.", error.Message); Assert.Null(error.InnerException);
    }
    [Fact]
    public void Private_extensions_duplicates_empty_and_noncanonical_identities_are_rejected()
    {
        const string valid = "{\"attachmentId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"cardId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"version\":2}";
        foreach (var json in new[] { valid.Replace("\"version\":2", "\"version\":2,\"sha256\":\"private\"", StringComparison.Ordinal),
            valid.Replace("\"version\":2", "\"version\":2,\"version\":3", StringComparison.Ordinal),
            valid.Replace("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", Guid.Empty.ToString("D"), StringComparison.Ordinal),
            valid.Replace("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", StringComparison.Ordinal),
            new string('x', 257) })
            Assert.Throws<InvalidOperationException>(() => AttachmentPreviewAttempt.Parse(json));
    }
    [Theory]
    [InlineData(44, 1, 1)][InlineData(8388609, 1, 1)][InlineData(70, 0, 1)]
    [InlineData(70, 1, 0)][InlineData(70, 1025, 1)][InlineData(70, 1, 1025)]
    public void Persisted_output_must_respect_fixed_encoding_bounds(long size, int width, int height) =>
        Assert.Throws<ArgumentException>(() => new AttachmentPreviewMeasurement(size, new string('a', 64), width, height));
    [Fact]
    public void Recovery_digest_is_canonical_and_excluded_from_serialization()
    {
        foreach (var digest in new[] { "", new string('a', 63), new string('A', 64), new string('g', 64) })
            Assert.Throws<ArgumentException>(() => new AttachmentPreviewMeasurement(70, digest, 1, 1));
        var output = new AttachmentPreviewMeasurement(70, new string('a', 64), 1, 1);
        Assert.DoesNotContain(output.Sha256, JsonSerializer.Serialize(output), StringComparison.Ordinal);
        Assert.Equal(output, new AttachmentPreviewMeasurement(70, new string('a', 64), 1, 1));
    }
}
