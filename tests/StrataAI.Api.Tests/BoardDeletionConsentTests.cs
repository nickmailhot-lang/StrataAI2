using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_18_Board_delete_receipt_recovery_is_same_intent_and_freshly_authorized(bool boardAdministrator)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        if (boardAdministrator) await store.UpsertBoardMemberAsync(f.Board, f.Recipient, BoardRole.Admin, DateTimeOffset.UtcNow, ct);
        using var archive = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var actor = boardAdministrator ? member : owner; var key = Guid.NewGuid().ToString();
        var path = $"/boards/{f.Board}?version=2&confirmed=true";
        using var deletion = await Mutate(actor, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode); var receipt = await deletion.Content.ReadAsStringAsync(ct);
        using var replay = await Mutate(actor, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        Assert.Null(await store.FindBoardAsync(f.Board, ct));
        Assert.Equal(3, (await store.FindBoardAsync(f.Board, ct, includeDeleted: true))!.Version);
        using var hidden = await actor.GetAsync($"/boards/{f.Board}", ct); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var changed = await Mutate(actor, HttpMethod.Delete, $"/boards/{f.Board}?version=2&confirmed=false", new { }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("idempotency_key_reused", (await changed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var fresh = await Mutate(actor, HttpMethod.Delete, path, new { }); Assert.Equal(HttpStatusCode.NotFound, fresh.StatusCode);
        if (boardAdministrator)
        {
            await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
            using var denied = await Mutate(actor, HttpMethod.Delete, path, new { }, key);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.DoesNotContain("deletedBy", await denied.Content.ReadAsStringAsync(ct));
        }
    }

    [Fact]
    public async Task PRD_18_Board_deletion_requires_archived_state_explicit_consent_and_current_administration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        using var active = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}?version=1&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.NotFound, active.StatusCode);
        using var archive = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var unauthorized = await Mutate(member, HttpMethod.Delete, $"/boards/{f.Board}?version=2", new { });
        Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
        Assert.DoesNotContain("delete_confirmation_required", await unauthorized.Content.ReadAsStringAsync(ct));
        foreach (var query in new[] { "", "&confirmed=false" })
        {
            using var denied = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}?version=2{query}", new { });
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Equal("delete_confirmation_required", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            var unchanged = await store.FindBoardAsync(f.Board, ct);
            Assert.Equal(BoardLifecycleState.Archived, unchanged!.LifecycleState); Assert.Equal(2, unchanged.Version); Assert.Null(unchanged.DeletedBy);
        }
        var service = app.Services.GetRequiredService<IWorkManagementService>();
        Assert.Equal("delete_confirmation_required", (await service.DeleteBoardAsync(f.Board, f.Owner, 2, "board-consent-test", ct)).ErrorCode);
        using var deletion = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}?version=2&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        var receipt = await deletion.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("deleted", receipt.GetProperty("lifecycleState").GetString());
        Assert.Equal(3, receipt.GetProperty("version").GetInt64()); Assert.Equal(f.Owner, receipt.GetProperty("deletedBy").GetGuid());
    }
}
