using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // Explicitly synthetic selection/publication adapter. Real PostgreSQL
    // immutable proof/public scope locks are tested in CardCoverDeliveryContract.
    private sealed class CoverSelectionFixture : ICardAttachmentCoverStore
    {
        public Guid Organization, Card; public Guid? Selected;
        public Task<Guid?> FindSelectedAsync(Guid organization, Guid card, CancellationToken ct)
            => Task.FromResult(organization == Organization && card == Card ? Selected : null);
        public Task<bool> AcquirePublicReadScopeAsync(Guid organization, Guid board, Guid list, Guid card, CancellationToken ct)
            => Task.FromResult(organization == Organization && card == Card);
        public Task<CardRecord?> SetAsync(Guid organization, Guid board, Guid card, Guid? attachment, long? sourceVersion,
            Guid? previous, long cardVersion, DateTimeOffset now, CancellationToken ct) => throw new InvalidOperationException("Synthetic selection fixture cannot execute cover commands.");
    }

    [Fact(Skip = "Private staging requires Linux.", SkipUnless = nameof(LinuxPrivateStagingSupported))]
    public async Task PRD_14_Selected_cover_HTTP_exposes_only_sanitized_PNG_and_rechecks_public_or_current_member_scope()
    {
        var ct = TestContext.Current.CancellationToken; var objects = new UploadObjects(); var selection = new CoverSelectionFixture();
        await using var app = UploadFactory(objects, downloads: true, images: true, covers: selection);
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Selected cover HTTP", null, null, DateTimeOffset.UtcNow, ct);
        selection.Organization = f.Organization; selection.Card = card.Id;
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        using var upload = FileRequest($"/cards/{card.Id}/attachments", bytes.Concat("PRIVATE ORIGINAL METADATA"u8.ToArray()).ToArray(), Guid.NewGuid(), "Private original filename.png");
        using var created = await member.SendAsync(upload, ct); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var file = (await created.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        var metadata = Assert.IsType<DownloadMetadata>(app.Services.GetRequiredService<IAttachmentMetadataStore>()); metadata.Clean = true;
        var reference = AttachmentObjectReference.ForPreview(f.Organization, Guid.NewGuid());
        var stored = await objects.WritePrivateAsync(reference, new MemoryStream(bytes, false), bytes.Length, ct);
        metadata.PublishedPreview = new(new(reference, stored.SizeBytes, stored.Sha256), 1, 1);
        var path = $"/cards/{card.Id}/cover/image";
        using (var unselected = await member.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.NotFound, unselected.StatusCode);
        Assert.Equal(0, objects.Reads); selection.Selected = file.Id;
        using (var privateAnonymous = await anonymous.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.NotFound, privateAnonymous.StatusCode);
        using (var privateForeign = await outsider.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.NotFound, privateForeign.StatusCode);
        using (var stale = await member.GetAsync(path + "?cardVersion=1", ct)) Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Equal(0, objects.Reads);
        using (var privateImage = await member.GetAsync(path + "?cardVersion=2", ct))
        { Assert.Equal(HttpStatusCode.OK, privateImage.StatusCode); Assert.Equal(bytes, await privateImage.Content.ReadAsByteArrayAsync(ct)); }
        using (var publish = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = "PUBLIC", version = 1 }))
            Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Range = new(0, 0);
        using var image = await anonymous.SendAsync(request, ct); Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal(bytes, await image.Content.ReadAsByteArrayAsync(ct)); Assert.Equal(bytes.Length, image.Content.Headers.ContentLength);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", image.Content.Headers.ContentDisposition!.DispositionType); Assert.Equal("cover.png", image.Content.Headers.ContentDisposition.FileName);
        Assert.True(image.Headers.CacheControl!.NoStore); Assert.True(image.Headers.CacheControl.Private);
        Assert.Equal("nosniff", Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(image.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("sandbox; default-src 'none'", Assert.Single(image.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("none", Assert.Single(image.Headers.AcceptRanges)); Assert.Null(image.Headers.ETag); Assert.Null(image.Content.Headers.LastModified);
        using (var original = await anonymous.GetAsync($"/cards/{card.Id}/attachments/{file.Id}/download", ct)) Assert.Equal(HttpStatusCode.Unauthorized, original.StatusCode);
        using (var privateMetadata = await anonymous.GetAsync($"/cards/{card.Id}/cover", ct)) Assert.Equal(HttpStatusCode.Unauthorized, privateMetadata.StatusCode);
        using (var privateCandidates = await anonymous.GetAsync($"/cards/{card.Id}/cover/candidates", ct)) Assert.Equal(HttpStatusCode.Unauthorized, privateCandidates.StatusCode);
        objects.CorruptReads = true;
        using (var corrupt = await anonymous.GetAsync(path, ct))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, corrupt.StatusCode); Assert.Null(corrupt.Content.Headers.ContentDisposition); }
        objects.CorruptReads = false;
        objects.AfterReadClosed = () => { selection.Selected = null; return Task.CompletedTask; };
        using (var removed = await anonymous.GetAsync(path, ct))
        { Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode); Assert.Null(removed.Content.Headers.ContentDisposition); }
        var reads = objects.Reads;
        using (var retry = await anonymous.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);
        Assert.Equal(reads, objects.Reads); selection.Selected = file.Id;
        objects.AfterReadClosed = async () =>
        {
            using var hidden = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = "PRIVATE", version = 2 });
            Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        };
        using (var withdrawn = await anonymous.GetAsync(path, ct))
        { Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode); Assert.Null(withdrawn.Content.Headers.ContentDisposition); }
    }
}
