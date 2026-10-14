using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private static JsonSerializerOptions ListMovementJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
    private static async Task<Guid> ListMoveDestination(HttpClient owner, Guid organization, CancellationToken ct)
    {
        using var response = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = organization, name = "Move destination" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task List_board_move_HTTP_preserves_all_lifecycles_orders_before_anchor_and_replays_original_result()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var destination = await ListMoveDestination(owner, f.Organization, ct);
        using var anchorResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{destination}/lists", new { name = "Anchor" });
        var anchor = (await anchorResponse.Content.ReadFromJsonAsync<BoardListRecord>(ListMovementJson(), ct))!;
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var originals = new List<CardRecord>();
        foreach (var state in new[] { WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted })
        {
            using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = state.ToString(), description = "Stable contents" });
            var card = (await created.Content.ReadFromJsonAsync<CardRecord>(ListMovementJson(), ct))!;
            if (state != WorkItemLifecycleState.Active)
            {
                using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 1 });
                Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
            }
            if (state == WorkItemLifecycleState.Deleted)
            {
                using var deleted = await Mutate(owner, HttpMethod.Delete, $"/cards/{card.Id}?version=2&confirmed=true", new { });
                Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            }
            var original = await store.FindCardAsync(card.Id, ct, includeDeleted: true);
            Assert.NotNull(original); originals.Add(original);
        }
        var request = new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = 1, beforeListId = anchor.Id };
        var key = Guid.NewGuid().ToString();
        using var moved = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move", request, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var acknowledgment = (await moved.Content.ReadFromJsonAsync<BoardListRecord>(ListMovementJson(), ct))!;
        Assert.Equal(f.List, acknowledgment.Id); Assert.Equal(destination, acknowledgment.BoardId); Assert.Equal(2, acknowledgment.Version);
        Assert.True(string.CompareOrdinal(acknowledgment.Rank, anchor.Rank) < 0);
        foreach (var original in originals)
        {
            var current = await store.FindCardAsync(original.Id, ct, includeDeleted: true);
            Assert.NotNull(current);
            Assert.Equal(original with { BoardId = destination, Version = original.Version + 1, UpdatedAt = current.UpdatedAt }, current);
        }
        using var oldBoard = await owner.GetAsync($"/boards/{f.Board}", ct);
        var oldSnapshot = await oldBoard.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.DoesNotContain(oldSnapshot.GetProperty("lists").EnumerateArray(), row => row.GetProperty("list").GetProperty("id").GetGuid() == f.List);
        using var replay = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move", request, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(acknowledgment, await replay.Content.ReadFromJsonAsync<BoardListRecord>(ListMovementJson(), ct));
        using var changed = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move",
            new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = 1 }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Contains("idempotency_key_reused", await changed.Content.ReadAsStringAsync(ct));
        Assert.Equal(acknowledgment, await store.FindListAsync(f.List, ct));
    }

    [Fact]
    public async Task List_board_move_HTTP_requires_both_Boards_and_withdraws_receipt_after_source_permission_loss()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var destination = await ListMoveDestination(owner, f.Organization, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var before = await store.FindListAsync(f.List, ct);
        var body = new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = 1 }; var key = Guid.NewGuid().ToString();
        using var denied = await Mutate(member, HttpMethod.Post, $"/lists/{f.List}/move", body, key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Move destination", await denied.Content.ReadAsStringAsync(ct));
        Assert.Equal(before, await store.FindListAsync(f.List, ct));
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{destination}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var moved = await Mutate(member, HttpMethod.Post, $"/lists/{f.List}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var committed = await store.FindListAsync(f.List, ct);
        using var revoked = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var replay = await Mutate(member, HttpMethod.Post, $"/lists/{f.List}/move", body, key);
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode); Assert.Equal(committed, await store.FindListAsync(f.List, ct));
        using var destinationStillVisible = await member.GetAsync($"/boards/{destination}", ct);
        Assert.Equal(HttpStatusCode.OK, destinationStillVisible.StatusCode);
    }

    [Fact]
    public async Task List_board_move_HTTP_refuses_stale_invalid_foreign_and_archived_contexts_without_mutation()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var destination = await ListMoveDestination(owner, f.Organization, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var original = await store.FindListAsync(f.List, ct);
        foreach (var version in new[] { 0L, -1, long.MaxValue, 99 })
        {
            using var denied = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move", new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = version });
            Assert.Equal(version == 99 ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Equal(original, await store.FindListAsync(f.List, ct));
        }
        using var otherOrganization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Foreign Organization" });
        var foreignOrganization = (await otherOrganization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var foreign = await ListMoveDestination(owner, foreignOrganization, ct);
        foreach (var target in new[] { foreign, f.Board, Guid.Empty })
        {
            using var denied = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move", new { sourceBoardId = f.Board, destinationBoardId = target, expectedVersion = 1 });
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Equal(original, await store.FindListAsync(f.List, ct));
        }
        using var archived = await Mutate(owner, HttpMethod.Post, $"/boards/{destination}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var inactive = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move", new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode); Assert.Equal(original, await store.FindListAsync(f.List, ct));
    }

    [Fact]
    public async Task List_board_move_HTTP_requires_authenticated_session_and_CSRF_header()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var destination = await ListMoveDestination(owner, f.Organization, ct);
        var body = new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = 1 };
        using var unsigned = await Mutate(anonymous, HttpMethod.Post, $"/lists/{f.List}/move", body);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        using var noCsrf = await owner.PostAsJsonAsync($"/lists/{f.List}/move", body, ct);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(f.Board, (await app.Services.GetRequiredService<IWorkManagementStore>().FindListAsync(f.List, ct))!.BoardId);
    }
}
