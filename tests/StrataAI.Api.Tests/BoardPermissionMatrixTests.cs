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
    public static IEnumerable<object[]> BoardPermissionMatrixCases()
    {
        foreach (var visibility in new[] { "PRIVATE", "ORGANIZATION", "PUBLIC" })
        foreach (var role in new[] { "OrganizationAdmin", "BoardAdmin", "BoardMember", "OrganizationMember", "FormerOrganizationMember", "Anonymous" })
            yield return [visibility, role];
    }

    // PERM-FR-002/003/007/008/010: actual HTTP reads, commands and retained state.
    [Theory]
    [MemberData(nameof(BoardPermissionMatrixCases))]
    public async Task PRD_05_HTTP_permission_matrix_preserves_visibility_and_explicit_edit_boundaries(string visibility, string role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        using var anonymous = app.CreateClient(); var f = await NotificationFixture(app, owner, recipient, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
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
            Assert.True((await store.FindBoardMemberAsync(f.Board, f.Recipient, ct))!.Active);
        }
        if (role == "BoardAdmin")
        {
            using var granted = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" });
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        }
        using var published = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility, version = 1 });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        using var createdCard = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Protected matrix Card" });
        Assert.Equal(HttpStatusCode.Created, createdCard.StatusCode);
        var card = (await createdCard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var destinationBoard = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Destination matrix Board", visibility = "PUBLIC" });
        var destination = (await destinationBoard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var destinationList = await Mutate(owner, HttpMethod.Post, $"/boards/{destination}/lists", new { name = "Destination matrix List" });
        var destinationListId = (await destinationList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var actor = role == "Anonymous" ? anonymous : recipient;
        var canEdit = role is "OrganizationAdmin" or "BoardAdmin" or "BoardMember";
        var canAdminister = role is "OrganizationAdmin" or "BoardAdmin";
        var canView = canEdit || visibility == "PUBLIC" || role == "OrganizationMember" && visibility == "ORGANIZATION";
        using var read = await actor.GetAsync($"/boards/{f.Board}", ct);
        Assert.Equal(canView ? HttpStatusCode.OK : HttpStatusCode.NotFound, read.StatusCode);
        if (canView)
        {
            var access = (await read.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access");
            Assert.True(access.GetProperty("canView").GetBoolean());
            Assert.Equal(canEdit, access.GetProperty("canEdit").GetBoolean());
            Assert.Equal(canAdminister, access.GetProperty("canAdminister").GetBoolean());
            Assert.Equal(canEdit, access.GetProperty("canMove").GetBoolean());
        }
        else Assert.DoesNotContain("Protected matrix Card", await read.Content.ReadAsStringAsync(ct));
        using var members = await actor.GetAsync($"/boards/{f.Board}/members", ct);
        Assert.Equal(role == "Anonymous" ? HttpStatusCode.Unauthorized
            : canAdminister ? HttpStatusCode.OK : HttpStatusCode.NotFound, members.StatusCode);
        var reader = app.Services.GetRequiredService<IWorkEventReader>();
        async Task<string> Snapshot()
        {
            using var response = await owner.GetAsync($"/boards/{f.Board}", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var source = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
            var snapshot = (await response.Content.ReadAsStringAsync(ct)) + JsonSerializer.Serialize(source.Events);
            foreach (var surface in new[] { "comments", "checklists", "attachments" })
            {
                using var retained = await owner.GetAsync($"/cards/{card}/{surface}", ct);
                Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
                snapshot += await retained.Content.ReadAsStringAsync(ct);
            }
            return snapshot;
        }
        var deniedStatus = role == "Anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound;
        var editKey = Guid.NewGuid().ToString();
        foreach (var operation in new[] { "Card", "Comment", "Checklist", "UrlAttachment", "List" })
        {
            var version = (await store.FindCardAsync(card, ct))!.Version;
            var baseline = await Snapshot();
            var (method, path, body) = operation switch
            {
                "Card" => (HttpMethod.Patch, $"/cards/{card}", (object)new { title = "Permitted matrix mutation", version }),
                "Comment" => (HttpMethod.Post, $"/cards/{card}/comments", new { content = "Matrix comment", cardVersion = version }),
                "Checklist" => (HttpMethod.Post, $"/cards/{card}/checklists", new { title = "Matrix checklist", cardVersion = version }),
                "UrlAttachment" => (HttpMethod.Post, $"/cards/{card}/attachments/url", new { title = "Matrix link", url = "https://example.test/matrix", cardVersion = version }),
                _ => (HttpMethod.Post, $"/boards/{f.Board}/lists", new { name = "Matrix List" }),
            };
            using var result = await Mutate(actor, method, path, body, operation == "Card" ? editKey : Guid.NewGuid().ToString());
            // COMMENT requires explicit Board participation even when the
            // Organization administrator override permits other editing.
            var permitted = canEdit && !(operation == "Comment" && role == "OrganizationAdmin");
            if (permitted)
            {
                Assert.True(result.IsSuccessStatusCode, $"{operation}: {result.StatusCode} {await result.Content.ReadAsStringAsync(ct)}");
                Assert.NotEqual(baseline, await Snapshot());
            }
            else
            {
                Assert.Equal(deniedStatus, result.StatusCode);
                Assert.DoesNotContain("Protected matrix Card", await result.Content.ReadAsStringAsync(ct));
                Assert.Equal(baseline, await Snapshot());
            }
        }
        var beforeMove = await Snapshot(); var moveVersion = (await store.FindCardAsync(card, ct))!.Version;
        using var moved = await Mutate(actor, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId, expectedVersion = moveVersion, sourceBoardId = f.Board });
        if (role == "OrganizationAdmin")
        {
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            Assert.Equal(destination, (await store.FindCardAsync(card, ct))!.BoardId);
        }
        else
        {
            Assert.Equal(deniedStatus, moved.StatusCode); Assert.Equal(beforeMove, await Snapshot());
        }
        if (!canAdminister)
        {
            var baseline = await Snapshot();
            using var forbiddenVisibility = await Mutate(actor, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = "PUBLIC", version = 2 });
            Assert.Equal(deniedStatus, forbiddenVisibility.StatusCode); Assert.Equal(baseline, await Snapshot());
        }
        else
        {
            var current = (await store.FindBoardAsync(f.Board, ct))!;
            var nextVisibility = visibility == "PUBLIC" ? "PRIVATE" : "PUBLIC";
            using var administration = await Mutate(actor, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = nextVisibility, version = current.Version });
            Assert.Equal(HttpStatusCode.OK, administration.StatusCode);
            var changed = (await store.FindBoardAsync(f.Board, ct))!;
            Assert.Equal(current.Version + 1, changed.Version);
            Assert.Equal(Enum.Parse<BoardVisibility>(nextVisibility, true), changed.Visibility);
        }
        if (role == "OrganizationMember")
        {
            using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "MEMBER" });
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
            using var recovered = await Mutate(recipient, HttpMethod.Patch, $"/cards/{card}", new { title = "Permitted matrix mutation", version = 1 }, editKey);
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            Assert.Equal(2, (await store.FindCardAsync(card, ct))!.Version);
            using var replay = await Mutate(recipient, HttpMethod.Patch, $"/cards/{card}", new { title = "Permitted matrix mutation", version = 1 }, editKey);
            Assert.Equal(await recovered.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
            Assert.Equal(2, (await store.FindCardAsync(card, ct))!.Version);
        }
    }
}
