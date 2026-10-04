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
        async Task<HttpResponseMessage> Change(HttpClient client, HttpMethod method, string key)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Add("X-StrataAI-Request", "1"); request.Headers.Add("Idempotency-Key", key);
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
        using var unstar = await Change(owner, HttpMethod.Delete, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.NoContent, unstar.StatusCode);
        using var replay = await Change(owner, HttpMethod.Put, key); Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        var recovered = await owner.GetFromJsonAsync<JsonElement>(path, ct);
        Assert.False(recovered.GetProperty("starred").GetBoolean()); Assert.Equal(2, recovered.GetProperty("version").GetInt64());
        Assert.Equal(createdAt, recovered.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True((await member.GetFromJsonAsync<JsonElement>(path, ct)).GetProperty("starred").GetBoolean());
        using var changed = await Change(owner, HttpMethod.Delete, key); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>($"/boards/{f.Board}", ct);
        Assert.Equal(before.GetProperty("board").GetRawText(), after.GetProperty("board").GetRawText());
        Assert.Equal(before.GetProperty("lists").GetRawText(), after.GetProperty("lists").GetRawText());
        using var absent = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, absent.StatusCode);
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
        using var revoked = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.DoesNotContain("starred", await revoked.Content.ReadAsStringAsync(ct));
    }
}
