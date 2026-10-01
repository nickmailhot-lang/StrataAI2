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
    public async Task Board_member_directory_is_bounded_and_seeks_without_duplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Paged Board members" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var board = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Paged members", visibility = "PRIVATE" });
        var id = (await board.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        // Synthetic Demo-store members exercise seek ordering, not production FK eligibility.
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        for (var n = 0; n < 52; n++) await store.UpsertBoardMemberAsync(id, Guid.NewGuid(), BoardRole.Member, DateTimeOffset.UtcNow, ct);
        using var first = await owner.GetAsync($"/boards/{id}/members", ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstRows = (await first.Content.ReadFromJsonAsync<JsonElement>(ct)).EnumerateArray().ToArray();
        Assert.Equal(50, firstRows.Length);
        var cursor = Assert.Single(first.Headers.GetValues("X-StrataAI-Next-Cursor"));
        Assert.Equal(firstRows[^1].GetProperty("userId").GetGuid(), Guid.Parse(cursor));
        using var second = await owner.GetAsync($"/boards/{id}/members?after={cursor}", ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondRows = (await second.Content.ReadFromJsonAsync<JsonElement>(ct)).EnumerateArray().ToArray();
        Assert.Equal(3, secondRows.Length); Assert.False(second.Headers.Contains("X-StrataAI-Next-Cursor"));
        var ids = firstRows.Concat(secondRows).Select(row => row.GetProperty("userId").GetGuid()).ToArray();
        Assert.Equal(53, ids.Distinct().Count()); Assert.Equal(ids.OrderBy(value => value), ids);
        using var invalid = await owner.GetAsync($"/boards/{id}/members?after=not-a-uuid", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_board_member_cursor", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
    }

    // PERM-FR-005/006, PRD-05-TC-03/07/08: demotion cannot bypass the
    // same last-explicit-admin safeguard already enforced by removal.
    [Fact]
    public async Task Sole_board_admin_demotion_is_rejected_without_changing_membership_and_owner_override_remains_available()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var admin = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(admin);
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Board admin continuity" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, adminId, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var board = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Admin continuity board", visibility = "PRIVATE" });
        var id = (await board.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var added = await Mutate(owner, HttpMethod.Patch, $"/boards/{id}/members/{adminId}", new { role = "ADMIN" }); Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{id}/members/{ownerId}", new { }); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var original = await store.FindBoardMemberAsync(id, adminId, ct);
        using var directory = await admin.GetAsync($"/boards/{id}/members", ct);
        Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        var key = Guid.NewGuid().ToString();
        for (var retry = 0; retry < 2; retry++)
        {
            using var denied = await Mutate(admin, HttpMethod.Patch, $"/boards/{id}/members/{adminId}", new { role = "MEMBER" }, key);
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            Assert.Equal("sole_board_admin", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            Assert.Equal(original, await store.FindBoardMemberAsync(id, adminId, ct));
        }
        using var allowed = await Mutate(owner, HttpMethod.Patch, $"/boards/{id}/members/{adminId}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(BoardRole.Member, (await store.FindBoardMemberAsync(id, adminId, ct))!.Role);
        using var deniedDirectory = await admin.GetAsync($"/boards/{id}/members", ct);
        Assert.Equal(HttpStatusCode.NotFound, deniedDirectory.StatusCode);
        using var ownerDirectory = await owner.GetAsync($"/boards/{id}/members", ct);
        Assert.Equal(HttpStatusCode.OK, ownerDirectory.StatusCode);
    }

    [Fact]
    public async Task Concurrent_board_admin_self_demotions_leave_one_explicit_administrator()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var first = app.CreateClient(); using var second = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(first); await RegisterAndLogin(second);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var clients = new[] { first, second }; var ids = new List<Guid>();
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Concurrent admin continuity" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var board = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Concurrent admin board", visibility = "PRIVATE" });
        var id = (await board.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        foreach (var client in clients)
        {
            var user = (await client.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid(); ids.Add(user);
            await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, user, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
            using var added = await Mutate(owner, HttpMethod.Patch, $"/boards/{id}/members/{user}", new { role = "ADMIN" }); Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{id}/members/{ownerId}", new { }); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var responses = await Task.WhenAll(clients.Select((client, index) => Mutate(client, HttpMethod.Patch,
            $"/boards/{id}/members/{ids[index]}", new { role = "MEMBER" }, Guid.NewGuid().ToString())));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Single(await app.Services.GetRequiredService<IWorkManagementStore>().ListBoardMembersAsync(id, ct),
                member => member.Active && member.Role == BoardRole.Admin);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
