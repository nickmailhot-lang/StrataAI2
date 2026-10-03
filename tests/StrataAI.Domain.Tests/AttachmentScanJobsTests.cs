using System.Globalization;
using System.Text.Json;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentScanJobsTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-03T12:00:00Z",CultureInfo.InvariantCulture);
    private static (AttachmentUploadRecord Upload, AttachmentFileRecord File) Published()
    {
        var reference=new AttachmentObjectReference(Guid.NewGuid(),Guid.NewGuid()); var card=Guid.NewGuid(); var actor=Guid.NewGuid();
        var intent=AttachmentUploadIntent.Prepare(reference.AttachmentId,reference.OrganizationId,card,actor,Guid.NewGuid(),7,"Private name",128,new string('a',64),Now.AddHours(1),Now);
        var lease=Guid.NewGuid(); intent.StartWrite(lease,Now.AddMinutes(5),Now); intent.RecordStored(lease,"image/png",128,new string('a',64),Now.AddMinutes(1)); intent.Publish(Now.AddMinutes(2));
        var metadata=AttachmentMetadataMapping.From(Attachment.QuarantineFile(reference.AttachmentId,reference.OrganizationId,card,actor,"Private name","image/png",128,reference.ObjectKey,new string('a',64),Now.AddMinutes(1)));
        return (AttachmentUploadRecord.From(intent),new(metadata,new(reference,128,new string('a',64))));
    }
    [Fact]
    public void Queue_contract_is_reference_only_and_deduplicates_by_persisted_attachment_revision()
    {
        var fixture=Published(); var first=AttachmentScanJobs.Create(fixture.Upload,fixture.File,fixture.Upload.UploaderId,"scan-first");
        var retry=AttachmentScanJobs.Create(fixture.Upload,fixture.File,fixture.Upload.UploaderId,"scan-retry");
        Assert.NotEqual(first.Id,retry.Id); Assert.Equal(first.IdempotencyKey,retry.IdempotencyKey);
        Assert.Equal(fixture.Upload.OrganizationId,first.OrganizationId); Assert.Equal(fixture.Upload.UploaderId,first.ActorId);
        Assert.Equal(AttachmentScanJobs.Type,first.JobType); Assert.Equal(AttachmentScanJobs.Service,first.ServiceIdentity);
        using var json=JsonDocument.Parse(first.SafeMetadataJson); Assert.Equal(3,json.RootElement.EnumerateObject().Count());
        Assert.Equal(new AttachmentScanAttempt(fixture.Upload.Id,fixture.Upload.CardId,fixture.File.Metadata.Version),AttachmentScanAttempt.Parse(first.SafeMetadataJson));
        Assert.DoesNotContain(fixture.Upload.DisplayName,first.SafeMetadataJson,StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Upload.ExpectedSha256,first.SafeMetadataJson,StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.File.Integrity.Reference.ObjectKey,first.SafeMetadataJson,StringComparison.Ordinal);
        Assert.DoesNotContain("image/png",first.SafeMetadataJson,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("prepared")][InlineData("no_publish")][InlineData("expired")][InlineData("wrong_actor")]
    [InlineData("foreign_card")][InlineData("foreign_tenant")][InlineData("clean")][InlineData("deleted")]
    [InlineData("wrong_digest")][InlineData("wrong_size")][InlineData("wrong_type")][InlineData("future_metadata")][InlineData("max_revision")]
    public void Unpublished_or_mismatched_private_metadata_cannot_create_a_scan_job(string mutation)
    {
        var (upload,file)=Published(); var actor=upload.UploaderId;
        switch(mutation)
        {
            case "prepared": upload=upload with {State=AttachmentUploadState.Prepared}; break;
            case "no_publish": upload=upload with {PublishedAt=null}; break;
            case "expired": upload=upload with {PublishedAt=upload.ExpiresAt}; break;
            case "wrong_actor": actor=Guid.NewGuid(); break;
            case "foreign_card": file=file with {Metadata=file.Metadata with {CardId=Guid.NewGuid()}}; break;
            case "foreign_tenant": file=file with {Metadata=file.Metadata with {OrganizationId=Guid.NewGuid()}}; break;
            case "clean": file=file with {Metadata=file.Metadata with {ScanStatus=AttachmentScanStatus.Clean}}; break;
            case "deleted": file=file with {Metadata=file.Metadata with {DeletedAt=Now.AddMinutes(1)}}; break;
            case "wrong_digest": file=file with {Integrity=new(file.Integrity.Reference,128,new string('b',64))}; break;
            case "wrong_size": file=file with {Integrity=new(file.Integrity.Reference,129,file.Integrity.Sha256)}; break;
            case "wrong_type": file=file with {Metadata=file.Metadata with {MimeType="image/jpeg"}}; break;
            case "future_metadata": file=file with {Metadata=file.Metadata with {CreatedAt=Now.AddMinutes(3),UpdatedAt=Now.AddMinutes(3)}}; break;
            case "max_revision": file=file with {Metadata=file.Metadata with {Version=long.MaxValue}}; break;
        }
        Assert.Throws<InvalidOperationException>(() => AttachmentScanJobs.Create(upload,file,actor,"scan"));
    }
    [Theory]
    [InlineData("")][InlineData("[]")][InlineData("null")][InlineData("{")]
    [InlineData("{\"attachmentId\":\"00000000-0000-0000-0000-000000000000\",\"cardId\":\"11111111-1111-1111-1111-111111111111\",\"version\":1}")]
    [InlineData("{\"attachmentId\":\"11111111-1111-1111-1111-111111111111\",\"cardId\":\"00000000-0000-0000-0000-000000000000\",\"version\":1}")]
    [InlineData("{\"attachmentId\":\"11111111-1111-1111-1111-111111111111\",\"cardId\":\"11111111-1111-1111-1111-111111111111\",\"version\":0}")]
    [InlineData("{\"attachmentId\":\"11111111-1111-1111-1111-111111111111\",\"cardId\":\"11111111-1111-1111-1111-111111111111\",\"version\":9223372036854775807}")]
    [InlineData("{\"attachmentId\":\"11111111-1111-1111-1111-111111111111\",\"cardId\":\"11111111-1111-1111-1111-111111111111\",\"version\":\"1\"}")]
    [InlineData("{\"attachmentId\":\"11111111-1111-1111-1111-111111111111\",\"cardId\":\"11111111-1111-1111-1111-111111111111\",\"version\":1,\"sha256\":\"private\"}")]
    [InlineData("{\"attachmentId\":\"11111111-1111-1111-1111-111111111111\",\"cardId\":\"11111111-1111-1111-1111-111111111111\",\"version\":1,\"version\":2}")]
    public void Malformed_or_extended_queue_references_have_one_fixed_safe_failure(string payload)
    {
        var error=Assert.Throws<InvalidOperationException>(() => AttachmentScanAttempt.Parse(payload));
        Assert.Equal("Attachment scan references are invalid.",error.Message); Assert.Null(error.InnerException);
    }
    [Fact]
    public void Oversized_payloads_and_private_correlation_injection_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => AttachmentScanAttempt.Parse(new string('x',257)));
        var fixture=Published();
        foreach(var correlation in new[] {"", " padded ","injected\nvalue",new string('x',65),"é","quoted\"value"})
            Assert.Throws<ArgumentException>(() => AttachmentScanJobs.Create(fixture.Upload,fixture.File,fixture.Upload.UploaderId,correlation));
    }
}
