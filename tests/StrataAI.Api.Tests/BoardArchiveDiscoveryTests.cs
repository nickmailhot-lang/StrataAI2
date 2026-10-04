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
    public async Task PRD_04_Board_archive_hides_active_discovery_restores_and_recovers_the_original_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var id = Guid.Parse("71000000-0000-4000-8000-000000000001");
        await app.Services.GetRequiredService<IWorkManagementStore>().CreateBoardAsync(f.Organization, f.Owner, id,
            "Lifecycle discovery", "Private body", BoardVisibility.Private, "COLOR", "blue", DateTimeOffset.UtcNow, ct);
        var directory = $"/organizations/{f.Organization}/boards";
        var initial = await owner.GetFromJsonAsync<JsonElement>(directory, ct);
        Assert.Contains(initial.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
        var key = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Archive(string commandKey, long version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/boards/{id}/archive") { Content = JsonContent.Create(new { version }) };
            request.Headers.Add("Idempotency-Key", commandKey); return await owner.SendAsync(request, ct);
        }
        using var archived = await Archive(key, 1); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        var bytes = await archived.Content.ReadAsStringAsync(ct);
        var hidden = await owner.GetFromJsonAsync<JsonElement>(directory, ct);
        Assert.DoesNotContain(hidden.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
        using var replay = await Archive(key, 1); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(bytes, await replay.Content.ReadAsStringAsync(ct));
        var current = await owner.GetFromJsonAsync<JsonElement>($"/boards/{id}", ct);
        Assert.Equal("archived", current.GetProperty("board").GetProperty("lifecycleState").GetString());
        Assert.True(current.GetProperty("access").GetProperty("canAdminister").GetBoolean());
        Assert.False(current.GetProperty("access").GetProperty("canEdit").GetBoolean());
        using var restored = await owner.PostAsJsonAsync($"/boards/{id}/restore", new { version = 2 }, ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var visible = await owner.GetFromJsonAsync<JsonElement>(directory, ct);
        Assert.Contains(visible.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id && item.GetProperty("version").GetInt64() == 3);
    }

    [Fact]
    public async Task PRD_18_Board_archive_directory_is_bounded_admin_filtered_and_excludes_active_deleted_and_private_bodies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var at = DateTimeOffset.UtcNow;
        for (var index = 1; index <= 54; index++)
        {
            var id = Guid.Parse($"70000000-0000-4000-8000-{index:D12}");
            await store.CreateBoardAsync(f.Organization, f.Owner, id, $"Archived directory {index}", "Private directory body",
                BoardVisibility.Public, "COLOR", "blue", at, ct);
            if (index != 53) await store.SetBoardLifecycleAsync(id, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, at.AddSeconds(1), ct);
            if (index == 54) await store.SetBoardLifecycleAsync(id, BoardLifecycleState.Archived, BoardLifecycleState.Deleted, 2, at.AddSeconds(2), ct, f.Owner);
            if (index == 1) await store.UpsertBoardMemberAsync(id, f.Recipient, BoardRole.Admin, at, ct);
        }
        var path = $"/organizations/{f.Organization}/archived-boards";
        using var response = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore); Assert.True(response.Headers.CacheControl.Private);
        var first = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(f.Organization, first.GetProperty("organizationId").GetGuid());
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetString(); Assert.NotNull(cursor);
        var last = await owner.GetFromJsonAsync<JsonElement>(path + "?after=" + cursor, ct);
        Assert.Equal(2, last.GetProperty("items").GetArrayLength()); Assert.Equal(JsonValueKind.Null, last.GetProperty("nextCursor").ValueKind);
        var rows = first.GetProperty("items").EnumerateArray().Concat(last.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(52, rows.Select(row => row.GetProperty("id").GetGuid()).Distinct().Count());
        foreach (var row in rows)
        {
            Assert.Equal(new[] { "archivedAt", "id", "name", "organizationId", "version" }, row.EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.Equal(2, row.GetProperty("version").GetInt64()); Assert.Equal(at.AddSeconds(1), row.GetProperty("archivedAt").GetDateTimeOffset());
        }
        Assert.DoesNotContain("Private directory body", first.GetRawText());
        var limited = await member.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.Single(limited.GetProperty("items").EnumerateArray());
        Assert.Equal("Archived directory 1", limited.GetProperty("items")[0].GetProperty("name").GetString());
        using var missing = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        await store.UpsertBoardMemberAsync(Guid.Parse("70000000-0000-4000-8000-000000000001"), f.Recipient, BoardRole.Member, at.AddSeconds(2), ct);
        var demoted = await member.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.Equal(0, demoted.GetProperty("items").GetArrayLength());
        foreach (var invalid in new[] { "", "not-a-cursor", Guid.Empty.ToString() })
        {
            using var denied = await owner.GetAsync(path + "?after=" + invalid, ct); Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, at.AddSeconds(3), ct);
        using var revoked = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.DoesNotContain("Archived directory", await revoked.Content.ReadAsStringAsync(ct));
    }
}
