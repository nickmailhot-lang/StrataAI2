using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_14_Cover_HTTP_requires_current_internal_authority_revisions_and_published_image()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Cover HTTP boundary", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/cover";
        using var unreadable = await outsider.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        using var loggedOut = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        using var response = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var view = (await response.Content.ReadFromJsonAsync<CardCoverView>(ct))!;
        Assert.Equal(card.Id, view.CardId); Assert.True(view.CanEdit); Assert.Null(view.AttachmentId); Assert.Null(view.AttachmentVersion);
        using var invalid = await Mutate(member, HttpMethod.Put, path, new { attachmentId = Guid.Empty, cardVersion = 1, attachmentVersion = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var key = Guid.NewGuid().ToString(); var empty = new { attachmentId = (Guid?)null, cardVersion = 1, attachmentVersion = (long?)null };
        using var removed = await Mutate(member, HttpMethod.Put, path, empty, key); Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var noOp = (await removed.Content.ReadFromJsonAsync<CardCoverChange>(ct))!; Assert.False(noOp.Changed); Assert.Equal(1, noOp.CardVersion);
        using var replay = await Mutate(member, HttpMethod.Put, path, empty, key); Assert.Equal(noOp, await replay.Content.ReadFromJsonAsync<CardCoverChange>(ct));
        using var created = await Mutate(member, HttpMethod.Post, $"/cards/{card.Id}/attachments/url", new { title = "URL is not a cover", url = "https://example.test/", cardVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); var link = (await created.Content.ReadFromJsonAsync<AttachmentChange>(ct))!;
        using var urlCover = await Mutate(member, HttpMethod.Put, path, new { attachmentId = link.Attachment.Id, cardVersion = 2, attachmentVersion = 1 });
        Assert.Equal(HttpStatusCode.NotFound, urlCover.StatusCode); Assert.Equal(2, (await work.FindCardAsync(card.Id, ct))!.Version);
        using var revoked = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }); Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var revokedRetry = await Mutate(member, HttpMethod.Put, path, empty, key); Assert.Equal(HttpStatusCode.NotFound, revokedRetry.StatusCode);
        using var revokedRead = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revokedRead.StatusCode);
    }
}
