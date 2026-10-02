using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Board_departure_removes_active_and_archived_Card_assignments_and_advances_only_affected_revisions()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); var fixture = await BoardInvitationFixtureAsync(app, ct);
        var service = app.Services.GetRequiredService<IWorkManagementService>(); var store = app.Services.GetRequiredService<IWorkManagementStore>();
        Assert.True((await service.SetBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, BoardRole.Member, "fixture", ct)).Succeeded);
        var now = DateTimeOffset.UtcNow; var list = await store.CreateListAsync(fixture.Board.Id, Guid.NewGuid(), "Departure list", null, now, ct);
        var active = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Assigned active", null, null, now, ct);
        var archived = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Assigned archived", null, null, now, ct);
        var untouched = await store.CreateCardAsync(list.Id, Guid.NewGuid(), "Unassigned", null, null, now, ct);
        Assert.True((await service.SetCardMemberAsync(active.Id, fixture.Recipient.Id, fixture.Owner.Id, true, 1, "fixture", ct)).Succeeded);
        Assert.True((await service.SetCardMemberAsync(active.Id, fixture.Owner.Id, fixture.Owner.Id, true, 2, "fixture", ct)).Succeeded);
        Assert.True((await service.SetCardMemberAsync(archived.Id, fixture.Recipient.Id, fixture.Owner.Id, true, 1, "fixture", ct)).Succeeded);
        Assert.True((await service.SetCardLifecycleAsync(archived.Id, fixture.Owner.Id, WorkItemLifecycleState.Archived, 2, "fixture", ct)).Succeeded);
        Assert.True((await service.RemoveBoardMemberAsync(fixture.Board.Id, fixture.Owner.Id, fixture.Recipient.Id, "fixture", ct)).Succeeded);
        Assert.Equal(4, (await store.FindCardAsync(active.Id, ct))!.Version); Assert.Equal(4, (await store.FindCardAsync(archived.Id, ct))!.Version);
        Assert.Equal(1, (await store.FindCardAsync(untouched.Id, ct))!.Version);
        var removed = await service.SetCardMemberAsync(active.Id, fixture.Recipient.Id, fixture.Owner.Id, false, 4, "fixture", ct);
        Assert.True(removed.Succeeded); Assert.False(removed.Value!.Changed);
        var self = await service.SetCardMemberAsync(active.Id, fixture.Owner.Id, fixture.Owner.Id, true, 4, "fixture", ct);
        Assert.True(self.Succeeded); Assert.False(self.Value!.Changed);
        Assert.Equal("card_not_found", (await service.SetCardMemberAsync(active.Id, fixture.Recipient.Id, fixture.Owner.Id, true, 4, "fixture", ct)).ErrorCode);
        Assert.NotNull(await app.Services.GetRequiredService<StrataAI.Application.Identity.IIdentityStore>().FindUserByIdAsync(fixture.Recipient.Id, ct));
    }

    [Fact]
    public async Task Card_members_support_multiple_assignees_current_revisions_noops_and_exact_retry_receipts()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); await RegisterAndLogin(owner); var board = await TelemetryBoard(owner, ct);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var teammate = app.CreateClient(); await RegisterAndLogin(teammate);
        var user = (await teammate.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var org = (await work.FindBoardAsync(board, ct))!.OrganizationId;
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, user, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var listReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Assignment List" });
        var list = (await listReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardReply = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Assignment Card", description = "Retained content" });
        var original = await cardReply.Content.ReadFromJsonAsync<JsonElement>(ct); var card = original.GetProperty("id").GetGuid();
        var path = $"/cards/{card}/members/{user}";
        using var ineligible = await Mutate(owner, HttpMethod.Put, path + "?version=1", new { }); Assert.Equal(HttpStatusCode.NotFound, ineligible.StatusCode);
        await work.UpsertBoardMemberAsync(board, user, BoardRole.Member, DateTimeOffset.UtcNow, ct);
        using var invalid = await Mutate(owner, HttpMethod.Put, path + "?version=0", new { }); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_card_member_version", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(owner, HttpMethod.Put, path + "?version=1", new { }, key); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var receipt = await first.Content.ReadAsStringAsync(ct); var change = JsonSerializer.Deserialize<JsonElement>(receipt);
        Assert.Equal(user, change.GetProperty("userId").GetGuid()); Assert.True(change.GetProperty("assigned").GetBoolean()); Assert.True(change.GetProperty("changed").GetBoolean());
        Assert.Equal(2, change.GetProperty("card").GetProperty("version").GetInt64());
        using var self = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/members/{actor}?version=2", new { }); Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        Assert.Equal(3, (await self.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("card").GetProperty("version").GetInt64());
        var assignees = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/members", ct);
        Assert.Equal(3, assignees.GetProperty("cardVersion").GetInt64()); Assert.Equal(card, assignees.GetProperty("cardId").GetGuid());
        Assert.Equal(board, assignees.GetProperty("boardId").GetGuid()); Assert.Equal(org, assignees.GetProperty("organizationId").GetGuid());
        Assert.True(assignees.GetProperty("canEdit").GetBoolean()); Assert.Equal(2, assignees.GetProperty("items").GetArrayLength());
        var options = await teammate.GetFromJsonAsync<JsonElement>($"/cards/{card}/member-options", ct);
        Assert.Equal(3, options.GetProperty("cardVersion").GetInt64()); Assert.Equal(card, options.GetProperty("cardId").GetGuid());
        Assert.Equal(org, options.GetProperty("organizationId").GetGuid()); Assert.Equal(board, options.GetProperty("boardId").GetGuid());
        Assert.Equal(2, options.GetProperty("items").GetArrayLength());
        Assert.All(options.GetProperty("items").EnumerateArray(), item => {
            Assert.True(item.GetProperty("assigned").GetBoolean());
            Assert.Equal(new[] { "assigned", "displayName", "userId" }, item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        });
        Assert.All(assignees.GetProperty("items").EnumerateArray(), item => {
            Assert.Equal(actor, item.GetProperty("assignedBy").GetGuid()); Assert.True(item.GetProperty("assignedAt").TryGetDateTimeOffset(out _));
            Assert.Equal(new[] { "assignedAt", "assignedBy", "displayName", "userId" }, item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        });
        using var retry = await Mutate(owner, HttpMethod.Put, path + "?version=1", new { }, key); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(receipt, await retry.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Delete, path + "?version=2", new { }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var noop = await Mutate(owner, HttpMethod.Put, path + "?version=3", new { }); Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        var unchanged = await noop.Content.ReadFromJsonAsync<JsonElement>(ct); Assert.False(unchanged.GetProperty("changed").GetBoolean()); Assert.Equal(3, unchanged.GetProperty("card").GetProperty("version").GetInt64());
        using var remove = await Mutate(owner, HttpMethod.Delete, path + "?version=3", new { }); Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        var removed = await remove.Content.ReadFromJsonAsync<JsonElement>(ct); Assert.False(removed.GetProperty("assigned").GetBoolean()); Assert.True(removed.GetProperty("changed").GetBoolean());
        Assert.Equal(4, removed.GetProperty("card").GetProperty("version").GetInt64());
        var remaining = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/members", ct);
        Assert.Equal(actor, Assert.Single(remaining.GetProperty("items").EnumerateArray()).GetProperty("userId").GetGuid());
        var afterRemoval = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/member-options", ct);
        Assert.Equal(4, afterRemoval.GetProperty("cardVersion").GetInt64());
        Assert.False(afterRemoval.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("userId").GetGuid() == user).GetProperty("assigned").GetBoolean());
        foreach (var property in new[] { "title", "description", "rank" }) Assert.Equal(original.GetProperty(property).GetString(), removed.GetProperty("card").GetProperty(property).GetString());
        using var reused = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/members/{actor}?version=1", new { }, key); Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        await work.RemoveBoardMemberAsync(board, user, DateTimeOffset.UtcNow, ct);
        using var revoked = await Mutate(owner, HttpMethod.Put, path + "?version=1", new { }, key); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }

    [Fact]
    public async Task Card_member_mutations_hide_private_scope_and_retire_receipts_after_parent_archival()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); await RegisterAndLogin(owner); var board = await TelemetryBoard(owner, ct);
        var user = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var listReply = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Private assignment parent" });
        var list = (await listReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardReply = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Private assignment title" });
        var card = (await cardReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        var path = $"/cards/{card}/members/{user}?version=1";
        using var denied = await Mutate(outsider, HttpMethod.Put, path, new { }); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var deniedRead = await outsider.GetAsync($"/cards/{card}/members?after=bad", ct); Assert.Equal(HttpStatusCode.NotFound, deniedRead.StatusCode);
        using var deniedOptions = await outsider.GetAsync($"/cards/{card}/member-options?after=bad", ct); Assert.Equal(HttpStatusCode.NotFound, deniedOptions.StatusCode);
        Assert.DoesNotContain("Private assignment", await denied.Content.ReadAsStringAsync(ct));
        var key = Guid.NewGuid().ToString(); using var assigned = await Mutate(owner, HttpMethod.Put, path, new { }, key); Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using var invalidRead = await owner.GetAsync($"/cards/{card}/members?after=bad", ct); Assert.Equal(HttpStatusCode.BadRequest, invalidRead.StatusCode);
        using var invalidOptions = await owner.GetAsync($"/cards/{card}/member-options?after=bad", ct); Assert.Equal(HttpStatusCode.BadRequest, invalidOptions.StatusCode);
        using var publish = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 1 }); Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using var publicRead = await outsider.GetAsync($"/cards/{card}/members", ct); Assert.Equal(HttpStatusCode.NotFound, publicRead.StatusCode);
        using var publicOptions = await outsider.GetAsync($"/cards/{card}/member-options", ct); Assert.Equal(HttpStatusCode.NotFound, publicOptions.StatusCode);
        var publicBoard = await outsider.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal(JsonValueKind.Null, publicBoard.GetProperty("cardMembers").ValueKind);
        var ownerBoard = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var preview = ownerBoard.GetProperty("cardMembers").GetProperty(card.ToString());
        Assert.Equal(1, preview.GetProperty("total").GetInt64());
        var indicator = Assert.Single(preview.GetProperty("items").EnumerateArray());
        Assert.Equal(user, indicator.GetProperty("userId").GetGuid());
        Assert.Equal(new[] { "displayName", "userId" }, indicator.EnumerateObject().Select(p => p.Name).Order().ToArray());
        using var anonymous = app.CreateClient(); using var anonymousRead = await anonymous.GetAsync($"/cards/{card}/members", ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        using var anonymousOptions = await anonymous.GetAsync($"/cards/{card}/member-options", ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymousOptions.StatusCode);
        var anonymousBoard = await anonymous.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal(JsonValueKind.Null, anonymousBoard.GetProperty("cardMembers").ValueKind);
        using var archive = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 }); Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var retry = await Mutate(owner, HttpMethod.Put, path, new { }, key); Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);
        using var archivedRead = await owner.GetAsync($"/cards/{card}/members", ct); Assert.Equal(HttpStatusCode.NotFound, archivedRead.StatusCode);
        using var archivedOptions = await owner.GetAsync($"/cards/{card}/member-options", ct); Assert.Equal(HttpStatusCode.NotFound, archivedOptions.StatusCode);
        var archivedBoard = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Empty(archivedBoard.GetProperty("cardMembers").EnumerateObject());
        using var remove = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}/members/{user}?version=2", new { }); Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
    }
}
