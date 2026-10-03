using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_15_TC_01_03_04_07_HTTPAuthorCommandsReconcileRevisionsAndRedactionWithoutBodyReplay()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Comments", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/comments"; var key = Guid.NewGuid().ToString(); var input = new CreateCardCommentInput("  Literal <script>\r\n🙂  ", 1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Post, path, new CreateCardCommentInput(" ", 1), key);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var created = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); Assert.True(created.Headers.CacheControl!.NoStore);
        var change = (await created.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        Assert.Equal(2, change.CardVersion); Assert.Equal("Literal <script>\n🙂", change.Comment.Content);
        using var replay = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(change, await replay.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        var childPath = $"{path}/{change.Comment.Id}";
        using var foreign = await Mutate(member, HttpMethod.Patch, childPath, new EditCardCommentInput("Foreign", 2, 1));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode); Assert.DoesNotContain("Literal", await foreign.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Patch, childPath, new EditCardCommentInput("Stale", 1, 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var edited = await Mutate(owner, HttpMethod.Patch, childPath, new EditCardCommentInput("Edited", 2, 1));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode); var updated = (await edited.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        Assert.Equal(3, updated.CardVersion); Assert.Equal(2, updated.Comment.Version); Assert.NotNull(updated.Comment.EditedAt);
        using var refusedDelete = await Mutate(owner, HttpMethod.Delete, childPath, new DeleteCardCommentInput(3, 2, false));
        Assert.Equal(HttpStatusCode.BadRequest, refusedDelete.StatusCode);
        var deleteKey = Guid.NewGuid().ToString(); var deleteInput = new DeleteCardCommentInput(3, 2, true);
        using var deleted = await Mutate(owner, HttpMethod.Delete, childPath, deleteInput, deleteKey);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); var tombstone = (await deleted.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        Assert.Null(tombstone.Comment.Content); Assert.Equal(4, tombstone.CardVersion); Assert.Equal(3, tombstone.Comment.Version);
        using var deleteReplay = await Mutate(owner, HttpMethod.Delete, childPath, deleteInput, deleteKey);
        Assert.Equal(tombstone, await deleteReplay.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        using var hiddenOldReceipt = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, hiddenOldReceipt.StatusCode); Assert.DoesNotContain("Literal", await hiddenOldReceipt.Content.ReadAsStringAsync(ct));
        var page = (await member.GetFromJsonAsync<CardCommentPage>(path, ct))!;
        Assert.Null(Assert.Single(page.Items).Content); Assert.Equal(f.Owner, page.Items[0].AuthorId); Assert.Equal(updated.Comment.EditedAt, page.Items[0].EditedAt);
        var events = (await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(f.Organization, f.Board, 0, 100, ct)).Events;
        Assert.Equal(3, events.Count(x => x.Event.EventType.StartsWith("COMMENT_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PRD_15_TC_05_10_HTTPArchiveAndMembershipRevocationRefuseOriginalCommandRecovery()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Comments", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/comments"; var key = Guid.NewGuid().ToString(); var input = new CreateCardCommentInput("Member owned", 1);
        using var created = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 2 }); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var deniedArchived = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.NotFound, deniedArchived.StatusCode);
        Assert.False((await member.GetFromJsonAsync<CardCommentPage>(path, ct))!.CanComment);
        using var restored = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/restore", new { version = 3 }); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var recovered = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        await work.RemoveBoardMemberAsync(f.Board, f.Recipient, DateTimeOffset.UtcNow, ct);
        using var deniedReceipt = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.NotFound, deniedReceipt.StatusCode);
        using var deniedRead = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, deniedRead.StatusCode);
        Assert.DoesNotContain("Member owned", await deniedReceipt.Content.ReadAsStringAsync(ct));
        Assert.Single((await owner.GetFromJsonAsync<CardCommentPage>(path, ct))!.Items);
    }
}
