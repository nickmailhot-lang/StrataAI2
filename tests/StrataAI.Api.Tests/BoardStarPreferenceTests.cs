using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_04_Board_star_reads_are_minimal_actor_scoped_and_replay_does_not_replace_other_preferences()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var path = $"/boards/{f.Board}/star";
        var before = await owner.GetFromJsonAsync<JsonElement>($"/boards/{f.Board}", ct);
        using var initial = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.True(initial.Headers.CacheControl!.NoStore); Assert.True(initial.Headers.CacheControl.Private);
        var minimal = await initial.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(new[] { "boardId", "createdAt", "organizationId", "starred", "updatedAt", "userId", "version" }, minimal.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(0, minimal.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, minimal.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, minimal.GetProperty("updatedAt").ValueKind);
        Assert.Equal(f.Owner, minimal.GetProperty("userId").GetGuid()); Assert.Equal(f.Organization, minimal.GetProperty("organizationId").GetGuid());
        Assert.Equal(f.Board, minimal.GetProperty("boardId").GetGuid()); Assert.False(minimal.GetProperty("starred").GetBoolean());
        foreach (var query in new[] { "", "?version=not-a-revision", "?version=-1", "?version=0.5" })
        {
            using var invalidRequest = new HttpRequestMessage(HttpMethod.Put, path + query);
            invalidRequest.Headers.Add("X-StrataAI-Request", "1");
            invalidRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var invalid = await owner.SendAsync(invalidRequest, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_board_star_version", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
        async Task<HttpResponseMessage> Change(HttpClient client, HttpMethod method, string key, long version = 0)
        {
            using var request = new HttpRequestMessage(method, path + "?version=" + version); request.Headers.Add("X-StrataAI-Request", "1"); request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request, ct);
        }
        var key = Guid.NewGuid().ToString(); using var starred = await Change(owner, HttpMethod.Put, key);
        Assert.Equal(HttpStatusCode.NoContent, starred.StatusCode);
        var own = await owner.GetFromJsonAsync<JsonElement>(path, ct); Assert.True(own.GetProperty("starred").GetBoolean());
        Assert.Equal(1, own.GetProperty("version").GetInt64());
        var createdAt = own.GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal(createdAt, own.GetProperty("updatedAt").GetDateTimeOffset());
        var theirs = await member.GetFromJsonAsync<JsonElement>(path, ct); Assert.False(theirs.GetProperty("starred").GetBoolean());
        Assert.Equal(f.Recipient, theirs.GetProperty("userId").GetGuid());
        using var memberStar = await Change(member, HttpMethod.Put, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.NoContent, memberStar.StatusCode);
        using var stale = await Change(owner, HttpMethod.Delete, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var unstar = await Change(owner, HttpMethod.Delete, Guid.NewGuid().ToString(), 1); Assert.Equal(HttpStatusCode.NoContent, unstar.StatusCode);
        using var replay = await Change(owner, HttpMethod.Put, key); Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using var changedRevision = await Change(owner, HttpMethod.Put, key, 2); Assert.Equal(HttpStatusCode.Conflict, changedRevision.StatusCode);
        var recovered = await owner.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.False(recovered.GetProperty("starred").GetBoolean()); Assert.Equal(2, recovered.GetProperty("version").GetInt64());
        Assert.Equal(createdAt, recovered.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True((await member.GetFromJsonAsync<JsonElement>(path, ct)).GetProperty("starred").GetBoolean());
        using var changed = await Change(owner, HttpMethod.Delete, key); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>($"/boards/{f.Board}", ct);
        Assert.Equal(before.GetProperty("board").GetRawText(), after.GetProperty("board").GetRawText());
        Assert.Equal(before.GetProperty("lists").GetRawText(), after.GetProperty("lists").GetRawText());
        var eventsPath = path + "/events";
        using var eventRead = await owner.GetAsync(eventsPath, ct);
        Assert.Equal(HttpStatusCode.OK, eventRead.StatusCode); Assert.True(eventRead.Headers.CacheControl!.NoStore);
        var journal = await eventRead.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(f.Owner, journal.GetProperty("userId").GetGuid());
        var history = journal.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new long[] { 1, 2 }, history.Select(e => e.GetProperty("version").GetInt64()).ToArray());
        Assert.Equal(2, history.Select(e => e.GetProperty("eventId").GetGuid()).Distinct().Count());
        Assert.Single(history.Select(e => e.GetProperty("entityId").GetGuid()).Distinct());
        foreach (var e in history)
        {
            Assert.Equal(f.Owner, e.GetProperty("actorId").GetGuid()); Assert.Equal(f.Board, e.GetProperty("boardId").GetGuid());
            Assert.Equal(f.Organization, e.GetProperty("organizationId").GetGuid());
            Assert.Equal("BOARD_STARRED", e.GetProperty("eventType").GetString());
            Assert.Equal("UserBoardPreference", e.GetProperty("entityType").GetString());
            Assert.Empty(e.GetProperty("metadata").EnumerateObject());
            Assert.False(e.TryGetProperty("starred", out _));
        }
        var memberHistory = await member.GetFromJsonAsync<JsonElement>(eventsPath, ct);
        Assert.Single(memberHistory.GetProperty("items").EnumerateArray());
        Assert.Equal(f.Recipient, memberHistory.GetProperty("items")[0].GetProperty("actorId").GetGuid());
        using var badCursor = await owner.GetAsync(eventsPath + "?after=invalid", ct); Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(eventsPath + "?after=2", ct)).GetProperty("items").EnumerateArray());
        var service = app.Services.GetRequiredService<StrataAI.Application.WorkManagement.IWorkManagementService>();
        for (long version = 2; version < 53; version++)
            Assert.True((await service.SetStarAsync(f.Board, f.Owner, version % 2 == 0, ct, version)).Succeeded);
        var firstPage = await owner.GetFromJsonAsync<JsonElement>(eventsPath, ct);
        Assert.Equal(50, firstPage.GetProperty("items").GetArrayLength()); Assert.Equal(50, firstPage.GetProperty("nextAfter").GetInt64());
        var tail = await owner.GetFromJsonAsync<JsonElement>(eventsPath + "?after=50", ct);
        Assert.Equal(new long[] { 51,52,53 }, tail.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("version").GetInt64()).ToArray());
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextAfter").ValueKind);
        using var absent = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, absent.StatusCode);
        using var noJournal = await anonymous.GetAsync(eventsPath, ct); Assert.Equal(HttpStatusCode.Unauthorized, noJournal.StatusCode);
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
        using var revoked = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        using var revokedJournal = await member.GetAsync(eventsPath + "?after=invalid", ct);
        Assert.Equal(HttpStatusCode.NotFound, revokedJournal.StatusCode);
        Assert.DoesNotContain("BOARD_STARRED", await revokedJournal.Content.ReadAsStringAsync(ct));
        Assert.DoesNotContain("starred", await revoked.Content.ReadAsStringAsync(ct));
    }
}
