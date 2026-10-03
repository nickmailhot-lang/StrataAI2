using System.Buffers.Binary;
using System.Text;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentUploadPolicyTests
{
    private static readonly AttachmentFileTypeInspector Inspector = new();
    private static byte[] Png(uint width = 10, uint height = 10)
    {
        var bytes = new byte[33]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13); "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), width); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8; bytes[25] = 6; return bytes;
    }
    private static byte[] Webp(uint declaredSize = 12)
    { var bytes = new byte[20]; "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), declaredSize); "WEBPVP8 "u8.CopyTo(bytes.AsSpan(8)); return bytes; }
    [Fact]
    public void PRD_14_TC_01_Type_policy_uses_actual_prefix_and_configured_allowlist_without_filename_or_client_MIME()
    {
        var all = new AttachmentUploadPolicy(1024, ["image/png", "image/jpeg", "image/webp", "application/pdf"]);
        Assert.Equal("image/png", all.AdmitPrefix(Inspector, Png()).MimeType);
        Assert.Equal("image/jpeg", all.AdmitPrefix(Inspector, [0xff, 0xd8, 0xff, 0xe1]).MimeType);
        Assert.Equal("application/pdf", all.AdmitPrefix(Inspector, "%PDF-2.0\n"u8).MimeType);
        var webp = all.AdmitPrefix(Inspector, Webp()); Assert.Equal("image/webp", webp.MimeType); Assert.Equal(20, webp.DeclaredContainerSizeBytes);
        var imagesOnly = new AttachmentUploadPolicy(1024, ["image/png"]);
        var failure = Assert.Throws<AttachmentUploadValidationException>(() => imagesOnly.AdmitPrefix(Inspector, "%PDF-1.7\n"u8));
        Assert.Equal("attachment_type_not_allowed", failure.Code);
    }
    [Theory]
    [InlineData("<svg onload='private'>")]
    [InlineData("<html>private upload</html>")]
    [InlineData("GIF89a")]
    [InlineData("MZprivate")]
    [InlineData("PKprivate")]
    [InlineData("%PDF-1.8\n")]
    [InlineData("%PDF-1.7private")]
    [InlineData("prefix %PDF-1.7\n")]
    public void PRD_14_TC_03_Unknown_active_executable_and_spoofed_headers_remain_disallowed(string prefix)
    {
        var policy = new AttachmentUploadPolicy(1024, ["image/png", "image/jpeg", "image/webp", "application/pdf"]);
        var failure = Assert.Throws<AttachmentUploadValidationException>(() => policy.AdmitPrefix(Inspector, Encoding.UTF8.GetBytes(prefix)));
        Assert.Equal("attachment_type_not_allowed", failure.Code); Assert.DoesNotContain("private", failure.ToString());
    }
    [Fact]
    public void PRD_14_TC_03_Truncated_or_unbounded_prefix_and_invalid_initial_image_shapes_are_rejected()
    {
        var policy = new AttachmentUploadPolicy(1024, ["image/png", "image/jpeg", "image/webp"]);
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[257], Png()[..8], Png(0), Png(10000, 10000), Webp(1) })
            Assert.Equal("attachment_type_not_allowed", Assert.Throws<AttachmentUploadValidationException>(() => policy.AdmitPrefix(Inspector, bytes)).Code);
        var invalidDepth = Png(); invalidDepth[24] = 1;
        Assert.Throws<AttachmentUploadValidationException>(() => policy.AdmitPrefix(Inspector, invalidDepth));
        var invalidFilter = Png(); invalidFilter[27] = 1;
        Assert.Throws<AttachmentUploadValidationException>(() => policy.AdmitPrefix(Inspector, invalidFilter));
        Assert.Equal("attachment_too_large", Assert.Throws<AttachmentUploadValidationException>(() => policy.AdmitPrefix(Inspector, Webp(1024))).Code);
    }
    [Fact]
    public void PRD_14_TC_03_Measured_receipts_require_original_scope_digest_actual_size_and_container_length()
    {
        var policy = new AttachmentUploadPolicy(128, ["image/png", "image/webp"]);
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); var digest = new string('a', 64);
        var probe = policy.AdmitPrefix(Inspector, Webp()); var measured = new StoredAttachmentObject(reference, 20, digest);
        policy.RequireMeasuredFile(reference, probe, measured);
        foreach (var invalid in new[] { measured with { SizeBytes = 0 }, measured with { SizeBytes = 19 }, measured with { Sha256 = "private-provider-value" },
            measured with { Sha256 = new string('A', 64) }, measured with { Reference = new(Guid.NewGuid(), reference.AttachmentId) } })
            Assert.Equal("attachment_integrity_invalid", Assert.Throws<AttachmentUploadValidationException>(() => policy.RequireMeasuredFile(reference, probe, invalid)).Code);
        Assert.Equal("attachment_too_large", Assert.Throws<AttachmentUploadValidationException>(() => policy.RequireMeasuredFile(reference, probe, measured with { SizeBytes = 129 })).Code);
        Assert.Equal("attachment_type_not_allowed", Assert.Throws<AttachmentUploadValidationException>(() => policy.RequireMeasuredFile(reference, new("application/pdf"), measured)).Code);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1073741825)]
    public void ARCH_07_TC_01_Invalid_upload_limits_fail_configuration(long maximum) => Assert.Throws<ArgumentOutOfRangeException>(() => new AttachmentUploadPolicy(maximum, ["image/png"]));
    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("IMAGE/PNG")]
    [InlineData("image/png; unsafe")]
    [InlineData("")]
    public void ARCH_07_TC_01_Unsupported_or_ambiguous_configured_types_do_not_expand_supported_policy(string type)
    { Assert.Throws<ArgumentException>(() => new AttachmentUploadPolicy(128, [type])); }
    [Fact]
    public void ARCH_07_TC_01_Empty_duplicate_and_oversized_type_lists_fail_configuration()
    {
        Assert.Throws<ArgumentException>(() => new AttachmentUploadPolicy(128, []));
        Assert.Throws<ArgumentException>(() => new AttachmentUploadPolicy(128, ["image/png", "image/png"]));
        Assert.Throws<ArgumentException>(() => new AttachmentUploadPolicy(128, Enumerable.Repeat("image/png", 10000)));
    }
}
