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
    public async Task PRD_03_Board_directory_filters_visibility_and_lifecycle_before_bounded_seek_and_refuses_revoked_members()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var now = DateTimeOffset.UtcNow;
        // Retire the fixture Board so the expected directory is deterministic.
        await work.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, now, ct);
        var ids = Enumerable.Range(1, 52).Select(n => Guid.Parse($"80000000-0000-4000-8000-{n:000000000000}")).ToArray();
        foreach (var id in ids)
            await work.CreateBoardAsync(f.Organization, f.Owner, id, $"Visible directory Board {id}", null,
                id == ids[^1] ? BoardVisibility.Private : BoardVisibility.Organization, "COLOR", null, now, ct);
        await work.UpsertBoardMemberAsync(ids[^1], f.Recipient, BoardRole.Member, now, ct);
        // More than a page of earlier hidden/archived entries must not consume
        // the cap, leak a name, or prevent traversal to all active visible rows.
        foreach (var index in Enumerable.Range(1, 51))
        {
            var hidden = Guid.Parse($"10000000-0000-4000-8000-{index:000000000000}");
            var archived = Guid.Parse($"20000000-0000-4000-8000-{index:000000000000}");
            await work.CreateBoardAsync(f.Organization, f.Owner, hidden, "Hidden private Board", null, BoardVisibility.Private, "COLOR", null, now, ct);
            await work.CreateBoardAsync(f.Organization, f.Owner, archived, "Archived Board metadata", null, BoardVisibility.Organization, "COLOR", null, now, ct);
            Assert.NotNull(await work.SetBoardLifecycleAsync(archived, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, now, ct));
        }
        var route = $"/organizations/{f.Organization}/boards/directory";
        using var first = await member.GetAsync(route, ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl!.Private); Assert.True(first.Headers.CacheControl.NoStore);
        var firstBody = await first.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("Hidden private Board", firstBody); Assert.DoesNotContain("Archived Board metadata", firstBody);
        using var firstJson = JsonDocument.Parse(firstBody);
        var page = firstJson.RootElement;
        Assert.Equal(f.Organization, page.GetProperty("organizationId").GetGuid());
        Assert.Equal(ids.Take(50).ToArray(), page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(ids[49], page.GetProperty("nextCursor").GetGuid());
        var tail = await member.GetFromJsonAsync<JsonElement>($"{route}?after={ids[49]}", ct);
        Assert.Equal(ids.Skip(50).ToArray(), tail.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);
        Assert.All(tail.GetProperty("items").EnumerateArray(), row => Assert.Equal(1, row.GetProperty("version").GetInt64()));
        using var ownerPage = await owner.GetAsync(route, ct);
        Assert.Contains("Hidden private Board", await ownerPage.Content.ReadAsStringAsync(ct));
        foreach (var cursor in new[] { "bad", Guid.Empty.ToString("D"), ids[0].ToString("N") })
        {
            using var invalid = await member.GetAsync($"{route}?after={cursor}", ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_board_directory_cursor", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            Assert.True(invalid.Headers.CacheControl!.NoStore);
        }
        using var foreign = await outsider.GetAsync(route, ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.DoesNotContain("Board metadata", await foreign.Content.ReadAsStringAsync(ct));
        Assert.Equal(OrganizationRemoveMemberResult.Removed, await organizations.RemoveMemberAsync(f.Organization, f.Recipient, now, ct));
        using var revoked = await member.GetAsync($"{route}?after={ids[49]}", ct);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.DoesNotContain("Visible directory Board", await revoked.Content.ReadAsStringAsync(ct));
        Assert.True(revoked.Headers.CacheControl!.NoStore);
        using var deleting = await owner.GetAsync(route, ct);
        Assert.Equal(HttpStatusCode.OK, deleting.StatusCode);
        Assert.True(await organizations.MarkDeletingAsync(f.Organization, 1, now, ct));
        using var inactive = await owner.GetAsync(route, ct);
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        Assert.DoesNotContain("Hidden private Board", await inactive.Content.ReadAsStringAsync(ct));
    }
}
