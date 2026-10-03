using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // This explicitly synthetic Clean projection supplies scanner state only.
    // Authentication, admission, private disk verification and HTTP are real;
    // real PostgreSQL/Worker scan publication has separate contracts.
    private sealed class DownloadMetadata(IAttachmentMetadataStore inner) : IAttachmentMetadataStore
    {
        public bool Clean;
        public AttachmentPublishedPreview? PublishedPreview;
        public Task<AttachmentPublishedPreview?> FindPublishedPreviewAsync(AttachmentFileRecord source, CancellationToken ct)
            => Task.FromResult(PublishedPreview);
        public Task<AttachmentMetadata> CreateUrlAttachmentAsync(Guid id, Guid organization, Guid card, Guid uploader, string title, string url, DateTimeOffset at, CancellationToken ct)
            => inner.CreateUrlAttachmentAsync(id, organization, card, uploader, title, url, at, ct);
        public Task<AttachmentMetadata> CreateFileAttachmentAsync(StoredAttachmentObject measured, Guid card, Guid uploader, string name, string mime, DateTimeOffset at, CancellationToken ct)
            => inner.CreateFileAttachmentAsync(measured, card, uploader, name, mime, at, ct);
        public Task<AttachmentMetadata?> FindAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
            => inner.FindAttachmentAsync(organization, card, attachment, ct);
        public Task<AttachmentMetadata?> FindLifecycleAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
            => inner.FindLifecycleAttachmentAsync(organization, card, attachment, ct);
        public Task<IReadOnlyList<AttachmentMetadata>> ListArchivedAttachmentsAsync(Guid organization, Guid card, DateTimeOffset? at, Guid? id, CancellationToken ct)
            => inner.ListArchivedAttachmentsAsync(organization, card, at, id, ct);
        public Task<AttachmentMetadata?> ChangeAttachmentLifecycleAsync(Guid organization, Guid card, Guid attachment, long version,
            AttachmentLifecycleState from, AttachmentLifecycleState to, Guid actor, DateTimeOffset now, CancellationToken ct)
            => inner.ChangeAttachmentLifecycleAsync(organization, card, attachment, version, from, to, actor, now, ct);
        public Task<IReadOnlyList<AttachmentMetadata>> ListAttachmentsAsync(Guid organization, Guid card, DateTimeOffset? at, Guid? id, CancellationToken ct)
            => inner.ListAttachmentsAsync(organization, card, at, id, ct);
        public async Task<AttachmentFileRecord?> FindFileAttachmentAsync(Guid organization, Guid card, Guid attachment, CancellationToken ct)
        {
            var file = await inner.FindFileAttachmentAsync(organization, card, attachment, ct);
            return file is null || !Clean ? file : file with { Metadata = file.Metadata with {
                ScanStatus = AttachmentScanStatus.Clean, ScannedAt = file.Metadata.UpdatedAt, Version = PublishedPreview is null ? 2 : 3 } };
        }
    }

    [Fact]
    public async Task PRD_14_Controlled_download_verifies_bytes_uses_safe_headers_and_refuses_quarantine_corruption_and_revocation()
    {
        if (!OperatingSystem.IsLinux()) return;
        var ct = TestContext.Current.CancellationToken; var objects = new UploadObjects(); await using var app = UploadFactory(objects, downloads: true);
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Download fixture", null, null, DateTimeOffset.UtcNow, ct);
        var bytes = new byte[512]; "%PDF-1.7\n"u8.CopyTo(bytes);
        using var upload = FileRequest($"/cards/{card.Id}/attachments", bytes, Guid.NewGuid(), "../Résumé.pdf");
        using var created = await member.SendAsync(upload, ct); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var file = (await created.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        var path = $"/cards/{card.Id}/attachments/{file.Id}/download";
        using var pendingOptions = await member.GetAsync(path + "-options", ct); Assert.Equal(HttpStatusCode.NotFound, pendingOptions.StatusCode);
        using var pending = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode); Assert.Equal(0, objects.Reads);
        var metadata = Assert.IsType<DownloadMetadata>(app.Services.GetRequiredService<IAttachmentMetadataStore>()); metadata.Clean = true;
        using var pdfPreview = await member.GetAsync($"/cards/{card.Id}/attachments/{file.Id}/preview", ct);
        Assert.Equal(HttpStatusCode.NotFound, pdfPreview.StatusCode); Assert.Equal(0, objects.Reads);
        var options = (await member.GetFromJsonAsync<AttachmentDownloadOptions>(path + "-options", ct))!;
        Assert.Equal(f.Organization, options.OrganizationId); Assert.Equal(f.Board, options.BoardId); Assert.Equal(card.Id, options.CardId);
        Assert.Equal(2, options.CardVersion); Assert.Equal(file.Id, options.AttachmentId); Assert.Equal(2, options.AttachmentVersion); Assert.Equal(f.Recipient, options.ActorId);
        Assert.Equal(0, objects.Reads);
        using var wrongActor = await member.GetAsync(path + $"?actorId={f.Owner}&attachmentVersion=2", ct); Assert.Equal(HttpStatusCode.NotFound, wrongActor.StatusCode);
        using var wrongVersion = await member.GetAsync(path + $"?actorId={f.Recipient}&attachmentVersion=1", ct); Assert.Equal(HttpStatusCode.NotFound, wrongVersion.StatusCode);
        Assert.Equal(0, objects.Reads);
        using var outsiderOptions = await outsider.GetAsync(path + "-options", ct); Assert.Equal(HttpStatusCode.NotFound, outsiderOptions.StatusCode);
        using var outside = await outsider.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        using var unauthenticated = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode); Assert.Equal(0, objects.Reads);
        using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Range = new(0, 0);
        using var downloaded = await member.SendAsync(request, ct); Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(bytes, await downloaded.Content.ReadAsByteArrayAsync(ct)); Assert.Equal(bytes.Length, downloaded.Content.Headers.ContentLength);
        Assert.Equal("application/octet-stream", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
        Assert.DoesNotContain("../", downloaded.Content.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Contains("Résumé.pdf", downloaded.Content.Headers.ContentDisposition.FileNameStar!, StringComparison.Ordinal);
        Assert.True(downloaded.Headers.CacheControl!.NoStore); Assert.True(downloaded.Headers.CacheControl.Private);
        Assert.Equal("nosniff", Assert.Single(downloaded.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("sandbox; default-src 'none'", Assert.Single(downloaded.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("none", Assert.Single(downloaded.Headers.AcceptRanges)); Assert.Null(downloaded.Headers.ETag); Assert.Null(downloaded.Content.Headers.LastModified);
        using var canonical = await member.GetAsync($"/attachments/{file.Id}/download?cardId={card.Id}", ct);
        Assert.Equal(HttpStatusCode.OK, canonical.StatusCode); Assert.Equal(bytes, await canonical.Content.ReadAsByteArrayAsync(ct));
        objects.CorruptReads = true;
        using var corrupt = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.ServiceUnavailable, corrupt.StatusCode);
        Assert.Null(corrupt.Content.Headers.ContentDisposition); Assert.DoesNotContain("%PDF", await corrupt.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        objects.CorruptReads = false;
        objects.AfterReadClosed = async () => { using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode); };
        using var revokedDuringPreparation = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revokedDuringPreparation.StatusCode);
        Assert.Null(revokedDuringPreparation.Content.Headers.ContentDisposition);
        var reads = objects.Reads;
        using var revokedRetry = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revokedRetry.StatusCode); Assert.Equal(reads, objects.Reads);
    }
}
