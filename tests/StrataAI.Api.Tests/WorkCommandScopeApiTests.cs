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
    // PRD-08/18: edits/moves/restoration respect parent state; delete is elevated.
    [Fact]
    public async Task Archived_parents_freeze_children_and_permanent_card_deletion_requires_administrator()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var actor = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var orgResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Lifecycle permissions" });
        var org = (await orgResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Lifecycle" });
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/members/{actor}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Source" });
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var destinationResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Destination" });
        var destination = (await destinationResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var cardResponse = await Mutate(member, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Protected" });
        var card = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var secondResponse = await Mutate(member, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Frozen child" });
        var secondId = (await secondResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var second = await store.FindCardAsync(secondId, ct);
        Assert.NotNull(second);
        using var archivedCard = await Mutate(member, HttpMethod.Post, $"/cards/{card}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedCard.StatusCode);
        using var deniedDelete = await Mutate(member, HttpMethod.Delete, $"/cards/{card}?version=2", new { });
        Assert.Equal(HttpStatusCode.NotFound, deniedDelete.StatusCode);
        using var archivedList = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedList.StatusCode);
        using var deniedRestore = await Mutate(member, HttpMethod.Post, $"/cards/{card}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.NotFound, deniedRestore.StatusCode);
        // A still-ACTIVE card under the archived source must not be edited/moved.
        using var deniedEdit = await Mutate(member, HttpMethod.Patch, $"/cards/{second.Id}", new { title = "Denied", version = 1 });
        Assert.Equal(HttpStatusCode.NotFound, deniedEdit.StatusCode);
        using var deniedMove = await Mutate(member, HttpMethod.Post, $"/cards/{second.Id}/move",
            new { destinationListId = destination, rank = RankToken.Initial(), expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.NotFound, deniedMove.StatusCode);
        Assert.Equal(second, await store.FindCardAsync(second.Id, ct));
        using var archivedBoard = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedBoard.StatusCode);
        using var deniedListRestore = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.NotFound, deniedListRestore.StatusCode);
        using var restoredBoard = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, restoredBoard.StatusCode);
        using var restoredList = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, restoredList.StatusCode);
        using var restoredCard = await Mutate(member, HttpMethod.Post, $"/cards/{card}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, restoredCard.StatusCode);
        using var rearchivedCard = await Mutate(member, HttpMethod.Post, $"/cards/{card}/archive", new { version = 3 });
        Assert.Equal(HttpStatusCode.OK, rearchivedCard.StatusCode);
        using var deleted = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=4&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    // PRD-04/07/08-TC-05/10, PRD-24: lifecycle denial covers fresh and retry intent.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleting_organization_denies_work_commands_and_replay_without_changing_children(bool keyed)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var createdOrganization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Deleting scope" });
        var organization = (await createdOrganization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct))
            .GetProperty("organization").GetProperty("id").GetGuid();
        using var createdBoard = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = organization, name = "Protected board", visibility = "PUBLIC" });
        var board = (await createdBoard.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var createdList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Protected list" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var originalKey = Guid.NewGuid().ToString();
        using var createdCard = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Protected card" }, originalKey);
        var card = (await createdCard.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var access = new BoardAccess(true, true, true, true);
        var before = JsonSerializer.Serialize(await store.GetSnapshotAsync(board, actor, access, ct));
        Assert.True(await app.Services.GetRequiredService<IOrganizationStore>()
            .MarkDeletingAsync(organization, 1, DateTimeOffset.UtcNow, ct));
        async Task Denied(HttpMethod method, string route, object body, string code, string? key = null)
        {
            using var response = await Mutate(owner, method, route, body,
                key ?? (keyed ? Guid.NewGuid().ToString() : null));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var failure = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            Assert.Equal(code, failure.GetProperty("code").GetString());
            Assert.DoesNotContain("Protected", failure.ToString());
        }
        await Denied(HttpMethod.Post, "/boards", new { organizationId = organization, name = "Denied" }, "organization_not_found");
        await Denied(HttpMethod.Post, $"/boards/{board}/lists", new { name = "Denied" }, "board_not_found");
        await Denied(HttpMethod.Post, $"/lists/{list}/cards", new { title = "Denied" }, "list_not_found");
        await Denied(HttpMethod.Post, $"/lists/{list}/cards", new { title = "Protected card" }, "list_not_found", originalKey);
        await Denied(HttpMethod.Patch, $"/boards/{board}", new { name = "Denied", version = 1 }, "board_not_found");
        await Denied(HttpMethod.Patch, $"/lists/{list}", new { name = "Denied", version = 1 }, "list_not_found");
        await Denied(HttpMethod.Patch, $"/cards/{card}", new { title = "Denied", version = 1 }, "card_not_found");
        await Denied(HttpMethod.Post, $"/cards/{card}/archive", new { version = 1 }, "card_not_found");
        foreach (var route in new[] { $"/boards/{board}", $"/boards/{board}/sync" })
        {
            using var response = await owner.GetAsync(route, ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Protected", await response.Content.ReadAsStringAsync(ct));
            using var visitor = app.CreateClient();
            using var anonymous = await visitor.GetAsync(route, ct);
            Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
            Assert.DoesNotContain("Protected", await anonymous.Content.ReadAsStringAsync(ct));
        }
        Assert.Equal(before, JsonSerializer.Serialize(await store.GetSnapshotAsync(board, actor, access, ct)));
    }
}
