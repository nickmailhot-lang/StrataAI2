using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_04_Board_copy_creates_private_independent_structure_and_recovers_only_under_current_admission()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var children = app.Services.GetRequiredService<IChecklistStore>(); var now = DateTimeOffset.UtcNow;
        var source = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Independent text", "Private description", null, now, ct);
        var checklist = await children.CreateAsync(f.Organization, source.Id, "Copied work", RankToken.After(null), now, ct);
        string? rank = null; ChecklistItemRecord? completedSource = null;
        for (var i = 0; i < 63; i++)
        {
            rank = RankToken.After(rank); var item = await children.CreateItemAsync(f.Organization, checklist.Id, $"Task {i}", rank, now, ct);
            if (i == 0) completedSource = await children.UpdateItemAsync(f.Organization, checklist.Id, item.Id, item.Text, true, now, f.Owner, 1, now, ct);
        }
        using var labelResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/labels", new { name = "Independent label", color = "blue" });
        Assert.Equal(HttpStatusCode.Created, labelResponse.StatusCode);
        var labelId = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var label = await Mutate(owner, HttpMethod.Put, $"/cards/{source.Id}/labels/{labelId}?version=1", new { });
        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        var before = await store.FindBoardAsync(f.Board, ct); var key = Guid.NewGuid().ToString();
        var input = new { name = "  Private copy  ", version = 1 }; var path = $"/boards/{f.Board}/copy";
        using var response = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var receipt = await response.Content.ReadAsStringAsync(ct);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct); var copyId = result.GetProperty("id").GetGuid();
        Assert.NotEqual(f.Board, copyId); Assert.Equal("Private copy", result.GetProperty("name").GetString());
        Assert.Equal("PRIVATE", result.GetProperty("visibility").GetString()); Assert.Equal(1, result.GetProperty("version").GetInt64());
        var snapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{copyId}", ct);
        var list = Assert.Single(snapshot.GetProperty("lists").EnumerateArray());
        Assert.NotEqual(f.List, list.GetProperty("list").GetProperty("id").GetGuid());
        var copied = Assert.Single(list.GetProperty("cards").EnumerateArray()); var cardId = copied.GetProperty("id").GetGuid();
        Assert.NotEqual(source.Id, cardId); Assert.Equal(source.Description, copied.GetProperty("description").GetString());
        Assert.Equal(source.Rank, copied.GetProperty("rank").GetString()); Assert.Equal(1, copied.GetProperty("version").GetInt64());
        var labels = await owner.GetFromJsonAsync<JsonElement>($"/cards/{cardId}/labels", ct);
        Assert.NotEqual(labelId, Assert.Single(labels.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var copiedChecklists = await children.ListAsync(f.Organization, cardId, null, null, ct);
        var summary = Assert.Single(copiedChecklists); var copiedChecklist = summary.Checklist;
        Assert.NotEqual(checklist.Id, copiedChecklist.Id); Assert.Equal(63, summary.Total); Assert.Equal(0, summary.Completed);
        Assert.True(completedSource!.Completed);
        var firstItems = await children.ListItemsAsync(f.Organization, copiedChecklist.Id, null, null, ct);
        Assert.Equal(51, firstItems.Count);
        Assert.All(firstItems, item => { Assert.False(item.Completed); Assert.Equal(1, item.Version); });
        var continuation = await children.ListItemsAsync(f.Organization, copiedChecklist.Id, firstItems[^1].Rank, firstItems[^1].Id, ct);
        Assert.Equal(12, continuation.Count);
        var members = await store.ListBoardMembersAsync(copyId, ct); Assert.Equal(f.Owner, Assert.Single(members).UserId);
        using var deniedTarget = await member.GetAsync($"/boards/{copyId}", ct); Assert.Equal(HttpStatusCode.NotFound, deniedTarget.StatusCode);
        Assert.Equal(before, await store.FindBoardAsync(f.Board, ct));
        using var renamed = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}", new { name = "New source name", version = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Post, path, input); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var changed = await Mutate(owner, HttpMethod.Post, path, new { name = "Changed intent", version = 1 }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var retired = await Mutate(owner, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.NotFound, retired.StatusCode);
    }

    [Fact]
    public async Task PRD_04_Board_copy_withholds_structure_from_viewers_and_anonymous_users()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        using var viewer = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "VIEWER" });
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        var path = $"/boards/{f.Board}/copy"; var input = new { name = "Protected copy", version = 1 };
        using var denied = await Mutate(member, HttpMethod.Post, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var anonymous = app.CreateClient(); using var missingAccount = await Mutate(anonymous, HttpMethod.Post, path, input);
        Assert.Equal(HttpStatusCode.Unauthorized, missingAccount.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Post, path, new { name = " ", version = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var boards = await store.ListVisibleBoardsAsync(f.Organization, f.Owner, true, ct); Assert.Single(boards);
    }
}
