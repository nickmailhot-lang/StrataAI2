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
        var candidatePath = path + "/candidates";
        using var candidateResponse = await member.GetAsync(candidatePath, ct); Assert.Equal(HttpStatusCode.OK, candidateResponse.StatusCode);
        Assert.True(candidateResponse.Headers.CacheControl!.NoStore);
        var candidates = (await candidateResponse.Content.ReadFromJsonAsync<CardCoverCandidatePage>(ct))!;
        Assert.Equal(card.Id, candidates.CardId); Assert.True(candidates.CanEdit); Assert.Empty(candidates.Items); Assert.Null(candidates.NextCursor);
        using var invalidCursor = await member.GetAsync(candidatePath + "?after=malformed", ct); Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        using var anonymousCandidates = await anonymous.GetAsync(candidatePath, ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymousCandidates.StatusCode);
        using var foreignCandidates = await outsider.GetAsync(candidatePath, ct); Assert.Equal(HttpStatusCode.NotFound, foreignCandidates.StatusCode);
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
        using var destinationResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Cover receipt destination", visibility = "PRIVATE" });
        var destination = (await destinationResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("id").GetGuid();
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{destination}/members/{f.Recipient}", new { role = "MEMBER" }); Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var destinationList = await Mutate(owner, HttpMethod.Post, $"/boards/{destination}/lists", new { name = "Cover receipt parent" });
        var list = (await destinationList.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("id").GetGuid();
        using var moved = await Mutate(member, HttpMethod.Post, $"/cards/{card.Id}/move", new { sourceBoardId = f.Board, destinationListId = list, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode); var current = (await work.FindCardAsync(card.Id, ct))!;
        using var movedRetry = await Mutate(member, HttpMethod.Put, path, empty, key); Assert.Equal(HttpStatusCode.OK, movedRetry.StatusCode);
        Assert.Equal(noOp, await movedRetry.Content.ReadFromJsonAsync<CardCoverChange>(ct)); Assert.Equal(current, await work.FindCardAsync(card.Id, ct));
        using var withdrawDestination = await Mutate(owner, HttpMethod.Delete, $"/boards/{destination}/members/{f.Recipient}", new { }); Assert.Equal(HttpStatusCode.NoContent, withdrawDestination.StatusCode);
        using var hiddenRetry = await Mutate(member, HttpMethod.Put, path, empty, key); Assert.Equal(HttpStatusCode.NotFound, hiddenRetry.StatusCode);
        using var restoreDestination = await Mutate(owner, HttpMethod.Patch, $"/boards/{destination}/members/{f.Recipient}", new { role = "MEMBER" }); Assert.Equal(HttpStatusCode.OK, restoreDestination.StatusCode);
        using var restoredRetry = await Mutate(member, HttpMethod.Put, path, empty, key); Assert.Equal(HttpStatusCode.OK, restoredRetry.StatusCode); Assert.Equal(noOp, await restoredRetry.Content.ReadFromJsonAsync<CardCoverChange>(ct));
        using var revoked = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }); Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var revokedRetry = await Mutate(member, HttpMethod.Put, path, empty, key); Assert.Equal(HttpStatusCode.NotFound, revokedRetry.StatusCode);
        using var currentRead = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, currentRead.StatusCode);
        using var currentCandidates = await member.GetAsync(candidatePath, ct); Assert.Equal(HttpStatusCode.OK, currentCandidates.StatusCode);
        Assert.Equal(current, await work.FindCardAsync(card.Id, ct));
    }
}
