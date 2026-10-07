using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    public static bool LinuxControlledDownloadMatrixSupported => OperatingSystem.IsLinux();

    // PERM-FR-002/007/008/010: public Board admission is not internal file admission.
    // This fixture declares synthetic Clean metadata/storage; Linux private staging,
    // cookie authorization, final admission and byte delivery use the actual host.
    [Theory(Skip = "Controlled private staging requires Linux.", SkipUnless = nameof(LinuxControlledDownloadMatrixSupported))]
    [MemberData(nameof(BoardPermissionMatrixCases))]
    public async Task PRD_05_Controlled_download_matrix_preserves_current_organization_and_visibility_admission(string visibility, string role)
    {
        var ct = TestContext.Current.CancellationToken;
        var objects = new UploadObjects(); await using var app = UploadFactory(objects, downloads: true);
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Private download matrix", null, null, DateTimeOffset.UtcNow, ct);
        var bytes = new byte[512]; "%PDF-1.7\n"u8.CopyTo(bytes);
        using var upload = FileRequest($"/cards/{card.Id}/attachments", bytes, Guid.NewGuid(), "Private matrix.pdf");
        using var created = await owner.SendAsync(upload, ct); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var file = (await created.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        Assert.Equal(1, objects.Writes); Assert.Equal(0, objects.Reads);
        var metadata = Assert.IsType<DownloadMetadata>(app.Services.GetRequiredService<IAttachmentMetadataStore>());
        if (role is "OrganizationMember" or "OrganizationAdmin")
        {
            using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }
        if (role == "OrganizationAdmin")
            await organizations.AddOrRestoreMemberAsync(f.Organization, f.Recipient, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        if (role == "FormerOrganizationMember")
        {
            await organizations.RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
            Assert.True((await work.FindBoardMemberAsync(f.Board, f.Recipient, ct))!.Active);
        }
        if (role == "BoardAdmin")
        {
            using var promoted = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" });
            Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        }
        using var published = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility, version = 1 });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var actor = role == "Anonymous" ? anonymous : recipient;
        var permitted = role is "OrganizationAdmin" or "BoardAdmin" or "BoardMember"
            || role == "OrganizationMember" && visibility is "ORGANIZATION" or "PUBLIC";
        var refusal = role == "Anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound;
        var path = $"/cards/{card.Id}/attachments/{file.Id}/download";
        foreach (var route in new[] { path + "-options", path })
        {
            using var pending = await actor.GetAsync(route, ct);
            Assert.Equal(refusal, pending.StatusCode); Assert.Equal(0, objects.Reads);
        }
        metadata.Clean = true;
        using var optionsResponse = await actor.GetAsync(path + "-options", ct);
        Assert.Equal(permitted ? HttpStatusCode.OK : refusal, optionsResponse.StatusCode);
        Assert.Equal(0, objects.Reads);
        if (permitted)
        {
            var options = (await optionsResponse.Content.ReadFromJsonAsync<AttachmentDownloadOptions>(ct))!;
            Assert.Equal(f.Organization, options.OrganizationId); Assert.Equal(f.Board, options.BoardId);
            Assert.Equal(card.Id, options.CardId); Assert.Equal(file.Id, options.AttachmentId);
            Assert.Equal(f.Recipient, options.ActorId); Assert.Equal(2, options.AttachmentVersion);
        }
        using var delivery = await actor.GetAsync(path, ct);
        Assert.Equal(permitted ? HttpStatusCode.OK : refusal, delivery.StatusCode);
        if (permitted)
        {
            Assert.Equal(bytes, await delivery.Content.ReadAsByteArrayAsync(ct)); Assert.Equal(1, objects.Reads);
            Assert.Equal("attachment", delivery.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal("application/octet-stream", delivery.Content.Headers.ContentType!.MediaType);
            Assert.True(delivery.Headers.CacheControl!.Private); Assert.True(delivery.Headers.CacheControl.NoStore);
            // Withdrawal after provider preparation must be rechecked before bytes/headers.
            objects.AfterReadClosed = async () =>
                await organizations.RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
            using var withdrawn = await actor.GetAsync(path, ct);
            Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode); Assert.Equal(2, objects.Reads);
            Assert.Null(withdrawn.Content.Headers.ContentDisposition);
            Assert.DoesNotContain("%PDF", await withdrawn.Content.ReadAsStringAsync(ct));
            using var retry = await actor.GetAsync(path, ct);
            Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode); Assert.Equal(2, objects.Reads);
        }
        else
        {
            Assert.Equal(0, objects.Reads); Assert.Null(delivery.Content.Headers.ContentDisposition);
            Assert.DoesNotContain("Private matrix.pdf", await optionsResponse.Content.ReadAsStringAsync(ct));
            Assert.DoesNotContain("%PDF", await delivery.Content.ReadAsStringAsync(ct));
        }
        Assert.Equal(1, objects.Writes);
    }
}
