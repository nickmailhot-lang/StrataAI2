using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    // Synthetic scan/publication projection; real sessions, command rollback,
    // Board-owned identities, private-byte verification and HTTP delivery run.
    [Fact]
    public async Task PRD_04_Board_images_own_published_PNGs_copy_independently_and_survive_source_attachment_archive()
    {
        if (!OperatingSystem.IsLinux()) return;
        var ct = TestContext.Current.CancellationToken; var objects = new UploadObjects();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());
        await using var app = UploadFactory(objects, downloads: true, images: true);
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Board image source", null, null, DateTimeOffset.UtcNow, ct);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        using var upload = FileRequest($"/cards/{card.Id}/attachments", bytes.Concat("PRIVATE ORIGINAL METADATA"u8.ToArray()).ToArray(), Guid.NewGuid(), "Original private image.png");
        using var uploaded = await member.SendAsync(upload, ct); Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var file = (await uploaded.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        var metadata = Assert.IsType<DownloadMetadata>(app.Services.GetRequiredService<IAttachmentMetadataStore>());
        var path = $"/boards/{f.Board}/background/image"; var input = new SelectBoardBackgroundImageInput(card.Id, file.Id, 3, 1);
        using (var unpublished = await Mutate(member, HttpMethod.Post, path, input)) Assert.Equal(HttpStatusCode.BadRequest, unpublished.StatusCode);
        metadata.Clean = true; var reference = AttachmentObjectReference.ForPreview(f.Organization, Guid.NewGuid());
        var stored = await objects.WritePrivateAsync(reference, new MemoryStream(bytes, false), bytes.Length, ct);
        metadata.PublishedPreview = new(new(reference, stored.SizeBytes, stored.Sha256), 1, 1);
        var key = Guid.NewGuid().ToString();
        using var selected = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var receipt = await selected.Content.ReadAsStringAsync(ct); var board = (await selected.Content.ReadFromJsonAsync<BoardRecord>(json, ct))!;
        Assert.Equal(2, board.Version); Assert.Equal("IMAGE", board.BackgroundType); Assert.True(Guid.TryParseExact(board.BackgroundValue, "D", out _));
        Assert.DoesNotContain(reference.ObjectKey, receipt, StringComparison.Ordinal); Assert.DoesNotContain(stored.Sha256, receipt, StringComparison.Ordinal);
        using (var hidden = await anonymous.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var image = await member.GetAsync(path + "?boardVersion=2", ct))
        {
            Assert.Equal(HttpStatusCode.OK, image.StatusCode); Assert.Equal(bytes, await image.Content.ReadAsByteArrayAsync(ct));
            Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType); Assert.True(image.Headers.CacheControl!.NoStore);
            Assert.Equal("background.png", image.Content.Headers.ContentDisposition!.FileName);
            Assert.Equal("nosniff", Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
        }
        using var archivedAttachment = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/attachments/{file.Id}/archive", new { cardVersion = 2, version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedAttachment.StatusCode);
        using var replay = await Mutate(member, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using (var retained = await member.GetAsync(path, ct)) { Assert.Equal(HttpStatusCode.OK, retained.StatusCode); Assert.Equal(bytes, await retained.Content.ReadAsByteArrayAsync(ct)); }
        using var copied = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/copy", new { name = "Independent image Board", version = 2 });
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode); var target = (await copied.Content.ReadFromJsonAsync<BoardRecord>(json, ct))!;
        Assert.Equal("IMAGE", target.BackgroundType); Assert.Equal(1, target.Version); Assert.NotEqual(board.BackgroundValue, target.BackgroundValue);
        var copyPath = $"/boards/{target.Id}/background/image";
        using (var denied = await member.GetAsync(copyPath, ct)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var archivedBoard = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 2 }); Assert.Equal(HttpStatusCode.OK, archivedBoard.StatusCode);
        using (var copyImage = await owner.GetAsync(copyPath, ct)) { Assert.Equal(HttpStatusCode.OK, copyImage.StatusCode); Assert.Equal(bytes, await copyImage.Content.ReadAsByteArrayAsync(ct)); }
        using (var sourceImage = await owner.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.NotFound, sourceImage.StatusCode);
        var reads = objects.Reads;
        using (var stale = await owner.GetAsync(copyPath + "?boardVersion=2", ct)) Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Equal(reads, objects.Reads); objects.CorruptReads = true;
        using (var corrupt = await owner.GetAsync(copyPath, ct)) Assert.Equal(HttpStatusCode.ServiceUnavailable, corrupt.StatusCode);
        objects.CorruptReads = false;
        using var publishedCopy = await Mutate(owner, HttpMethod.Patch, $"/boards/{target.Id}/visibility", new { visibility = "PUBLIC", version = 1 });
        Assert.Equal(HttpStatusCode.OK, publishedCopy.StatusCode);
        using (var publicImage = await anonymous.GetAsync(copyPath + "?boardVersion=2", ct))
        {
            Assert.Equal(HttpStatusCode.OK, publicImage.StatusCode);
            Assert.Equal(bytes, await publicImage.Content.ReadAsByteArrayAsync(ct));
            Assert.True(publicImage.Headers.CacheControl!.NoStore);
        }
        using var narrowedCopy = await Mutate(owner, HttpMethod.Patch, $"/boards/{target.Id}/visibility", new { visibility = "PRIVATE", version = 2 });
        Assert.Equal(HttpStatusCode.OK, narrowedCopy.StatusCode); reads = objects.Reads;
        using (var withdrawn = await anonymous.GetAsync(copyPath, ct)) Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
        Assert.Equal(reads, objects.Reads);
        using var clearedCopy = await Mutate(owner, HttpMethod.Patch, $"/boards/{target.Id}", new { name = target.Name,
            version = 3, backgroundType = "COLOR", backgroundValue = (string?)null });
        Assert.Equal(HttpStatusCode.OK, clearedCopy.StatusCode); reads = objects.Reads;
        using (var retired = await owner.GetAsync(copyPath, ct)) Assert.Equal(HttpStatusCode.NotFound, retired.StatusCode);
        Assert.Equal(reads, objects.Reads);
    }
}
